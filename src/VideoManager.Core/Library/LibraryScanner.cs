using System.IO;
using VideoManager.Core.Models;
using VideoManager.Core.Parsing;
using VideoManager.Core.Util;

namespace VideoManager.Core.Library;

public sealed record ScanProgress(string Message, int Done, int Total);

public sealed record ScanResult(IReadOnlyList<Series> Series, int FileCount, int Skipped, TimeSpan Elapsed);

/// <summary>扫描媒体库根目录，解析文件名并归组为「剧集」。</summary>
public static class LibraryScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".ts", ".m2ts", ".wmv", ".mov", ".flv", ".webm", ".m4v", ".rmvb", ".rm",
        ".mpg", ".mpeg", ".vob", ".ogv", ".3gp", ".divx", ".asf", ".f4v",
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    private static readonly EnumerationOptions EnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public static ScanResult Scan(IEnumerable<LibraryRoot> roots, IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
        var started = DateTime.UtcNow;
        var groups = new Dictionary<string, GroupBuilder>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var fileCount = 0;

        var activeRoots = roots.Where(r => r.Enabled).ToList();
        var rootFiles = new Dictionary<char, List<(string file, string root)>>();
        var i = 0;

        foreach (var root in activeRoots)
        {
            token.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new ScanProgress($"正在扫描 {root.Name} …", i, activeRoots.Count));

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root.Path, "*", EnumOptions);
            }
            catch (Exception)
            {
                skipped++;
                continue;
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                if (!IsVideo(file))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    fileCount++;
                    AddFile(file, info, root, groups);
                }
                catch (Exception)
                {
                    skipped++;
                }
            }
        }

        var merged = MergeByTitle(groups.Values);
        var series = new List<Series>(merged.Count);
        foreach (var g in merged)
        {
            var s = g.ToSeries();
            if (s.Files.Count == 0)
            {
                continue;
            }
            s.Files.Sort(CompareFiles);
            s.SeasonLabel = BuildSeasonLabel(s.Files);
            series.Add(s);
        }

        series.Sort((a, b) =>
        {
            var c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : NaturalComparer.Instance.Compare(a.FolderPath, b.FolderPath);
        });

        var elapsed = DateTime.UtcNow - started;
        progress?.Report(new ScanProgress($"扫描完成：{series.Count} 部作品，{fileCount} 个视频", 1, 1));
        return new ScanResult(series, fileCount, skipped, elapsed);
    }

    private static void AddFile(string file, FileInfo info, LibraryRoot root, Dictionary<string, GroupBuilder> groups)
    {
        var folder = Path.GetDirectoryName(file) ?? root.Path;
        var fileName = Path.GetFileName(file);
        var parsed = NameParser.Parse(fileName);

        // 向上找出「剧集目录」：跳过 S2 / Season 3 / 第二季 这种季目录
        var showFolder = folder;
        int? folderSeason = null;
        var isRootLevel = string.Equals(NormalizeDir(showFolder), NormalizeDir(root.Path), StringComparison.OrdinalIgnoreCase);

        while (!string.Equals(NormalizeDir(showFolder), NormalizeDir(root.Path), StringComparison.OrdinalIgnoreCase))
        {
            var season = NameParser.ParseSeasonFolder(Path.GetFileName(showFolder));
            if (season is null)
            {
                break;
            }
            folderSeason ??= season;
            var parent = Path.GetDirectoryName(showFolder);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            showFolder = parent;
        }

        isRootLevel = string.Equals(NormalizeDir(showFolder), NormalizeDir(root.Path), StringComparison.OrdinalIgnoreCase);

        var showName = Path.GetFileName(showFolder.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(showName))
        {
            showName = root.Name;
        }

        var title = ChooseTitle(showName, parsed.ShowTitle, isRootLevel);
        if (title.Length == 0)
        {
            title = showName;
        }

        // 根目录下的散装文件：按「剧名」归组（同名的不同集数会聚成一部）
        var key = isRootLevel
            ? Path.Combine(NormalizeDir(root.Path), "::" + NormalizeTitle(title))
            : NormalizeDir(showFolder);

        if (!groups.TryGetValue(key, out var g))
        {
            g = new GroupBuilder(key, root.Path, isRootLevel ? root.Path : showFolder);
            groups[key] = g;
        }

        g.Titles[title] = g.Titles.GetValueOrDefault(title) + 1;

        var media = BuildMedia(file, info, root, parsed, parsed.Season ?? folderSeason, folder);
        g.Files.Add(media);
    }

    private static MediaFile BuildMedia(string file, FileInfo info, LibraryRoot root, ParsedName parsed, int? season, string folder)
    {
        return new MediaFile
        {
            FullPath = file,
            FileName = Path.GetFileName(file),
            FolderPath = folder,
            RootPath = root.Path,
            ShowTitle = ChineseConverter.ToSimplified(parsed.ShowTitle),
            EpisodeTitle = ChineseConverter.ToSimplified(parsed.EpisodeTitle),
            Season = season,
            Episode = parsed.Episode,
            SpecialNumber = parsed.SpecialNumber,
            IsSpecial = parsed.IsSpecial,
            ReleaseGroup = parsed.ReleaseGroup,
            SizeBytes = info.Length,
            ModifiedUtc = info.LastWriteTimeUtc,
        };
    }

    /// <summary>挑一个最像「剧名」的候选，并把繁体字形统一成简体。</summary>
    private static string ChooseTitle(string folderName, string? parsedTitle, bool folderIsRoot)
        => ChineseConverter.ToSimplified(ChooseTitleCore(folderName, parsedTitle, folderIsRoot));

    private static string ChooseTitleCore(string folderName, string? parsedTitle, bool folderIsRoot)
    {
        var folder = (folderName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = "未命名";
        }

        if (string.IsNullOrWhiteSpace(parsedTitle))
        {
            return folder;
        }

        var pt = parsedTitle!.Trim();

        // 直接位于根目录：文件名里的剧名通常才是最准确的
        if (folderIsRoot)
        {
            return pt;
        }

        // 文件夹名是中文，一般就是剧名
        if (NameParser.ContainsCjk(folder) || folder.Length >= 12)
        {
            return folder;
        }

        // 文件夹名很短且是拉丁字母（idol / video …），而文件名给出了更完整的名字
        if (pt.Length > folder.Length + 2)
        {
            return pt;
        }

        return folder;
    }

    private static List<GroupBuilder> MergeByTitle(IEnumerable<GroupBuilder> groups)
    {
        var byRoot = groups.GroupBy(g => g.RootPath, StringComparer.OrdinalIgnoreCase);
        var result = new List<GroupBuilder>();

        foreach (var rootGroup in byRoot)
        {
            var buckets = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);
            foreach (var g in rootGroup)
            {
                var title = g.BestTitle();
                var norm = NormalizeTitle(title);
                if (!buckets.TryGetValue(norm, out var target))
                {
                    buckets[norm] = g;
                    result.Add(g);
                    continue;
                }

                // 合并到已有分组；保留文件更多的那个作为主目录
                if (g.Files.Count > target.Files.Count)
                {
                    foreach (var f in target.Files)
                    {
                        g.Files.Add(f);
                    }
                    foreach (var kv in target.Titles)
                    {
                        g.Titles[kv.Key] = g.Titles.GetValueOrDefault(kv.Key) + kv.Value;
                    }
                    target.Files.Clear();
                    target.Titles.Clear();
                    result.Remove(target);
                    result.Add(g);
                    buckets[norm] = g;
                }
                else
                {
                    target.Files.AddRange(g.Files);
                    foreach (var kv in g.Titles)
                    {
                        target.Titles[kv.Key] = target.Titles.GetValueOrDefault(kv.Key) + kv.Value;
                    }
                    g.Files.Clear();
                    g.Titles.Clear();
                }
            }
        }

        return result.Where(r => r.Files.Count > 0).ToList();
    }

    public static string NormalizeTitle(string title)
    {
        // 先统一字形再比较，否则「偵探」和「侦探」会被当成两部不同的作品。
        var simplified = ChineseConverter.ToSimplified(title);
        var sb = new System.Text.StringBuilder(simplified.Length);
        foreach (var c in simplified)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    private static string NormalizeDir(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    public static int CompareFiles(MediaFile a, MediaFile b)
    {
        var sa = a.Season ?? int.MaxValue;
        var sb = b.Season ?? int.MaxValue;
        if (sa != sb) return sa.CompareTo(sb);

        // 特别篇排在本季正片后面
        if (a.IsSpecial != b.IsSpecial) return a.IsSpecial ? 1 : -1;

        var ea = a.IsSpecial ? a.SpecialNumber ?? a.Episode : a.Episode;
        var eb = b.IsSpecial ? b.SpecialNumber ?? b.Episode : b.Episode;
        var na = ea ?? int.MaxValue;
        var nb = eb ?? int.MaxValue;
        if (na != nb) return na.CompareTo(nb);

        return NaturalComparer.Instance.Compare(a.FileName, b.FileName);
    }

    /// <summary>按现有文件重算季标签，删除几集之后也能用。</summary>
    public static string ComputeSeasonLabel(IReadOnlyCollection<MediaFile> files)
        => BuildSeasonLabel(files.ToList());

    private static string BuildSeasonLabel(List<MediaFile> files)
    {
        var seasons = files.Where(f => f.Season is > 0).Select(f => f.Season!.Value).Distinct().OrderBy(s => s).ToList();
        if (seasons.Count == 0)
        {
            return files.Count > 0 ? $"{files.Count} 集" : "";
        }
        if (seasons.Count == 1)
        {
            return $"S{seasons[0]} · {files.Count} 集";
        }
        return $"S{seasons[0]}–S{seasons[^1]} · {files.Count} 集";
    }

    private sealed class GroupBuilder(string key, string rootPath, string folderPath)
    {
        public string Key { get; } = key;
        public string RootPath { get; } = rootPath;
        public string FolderPath { get; } = folderPath;
        public List<MediaFile> Files { get; } = [];
        public Dictionary<string, int> Titles { get; } = new(StringComparer.Ordinal);

        public string BestTitle()
        {
            if (Titles.Count == 0)
            {
                var n = Path.GetFileName(FolderPath.TrimEnd('\\', '/'));
                return ChineseConverter.ToSimplified(string.IsNullOrWhiteSpace(n) ? "未命名" : n);
            }
            return ChineseConverter.ToSimplified(
                Titles.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Length).First().Key);
        }

        public Series ToSeries()
        {
            var title = BestTitle();
            var series = new Series
            {
                Key = Key,
                Title = title,
                OriginalTitle = title,
                RootPath = RootPath,
                FolderPath = FolderPath,
            };
            series.Files.AddRange(Files);
            return series;
        }
    }
}
