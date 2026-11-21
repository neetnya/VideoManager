using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoManager.Core.Models;

namespace VideoManager.Core.Storage;

/// <summary>配置 + 轻量缓存（JSON）。默认放在程序（exe）所在目录，跟着程序走。</summary>
public sealed class AppStorage
{
    private readonly object _settingsLock = new();
    private readonly object _cacheLock = new();

    /// <summary>老版本的存放位置（%LOCALAPPDATA%\VideoManager），仅用于一次性搬家。</summary>
    private static string LegacyDataDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoManager");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // WindowLeft / WindowTop 的默认值是 double.NaN（还没存过窗口位置）。
        // System.Text.Json 默认拒绝把 NaN 写成 JSON 数字，会直接抛异常 ——
        // 而 SaveSettings 的 catch 又会把异常吞掉，结果就是「配置永远存不下来」。
        // 允许 "NaN" 这样的具名浮点字面量，读写都不会再炸。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string DataDirectory { get; }
    public string CoverDirectory { get; }
    public string SettingsFile => System.IO.Path.Combine(DataDirectory, "settings.json");
    public string CacheFile => System.IO.Path.Combine(DataDirectory, "library-cache.json");

    public AppStorage(string? dataDirectory = null)
    {
        var preferred = dataDirectory is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        DataDirectory = preferred;
        CoverDirectory = System.IO.Path.Combine(DataDirectory, "covers");

        if (TryPrepare(DataDirectory, CoverDirectory))
        {
            return;
        }

        // 程序目录不可写（例如装在 Program Files 下、或 U 盘只读），退回老位置，至少还能用
        DataDirectory = LegacyDataDirectory;
        CoverDirectory = System.IO.Path.Combine(DataDirectory, "covers");
        TryPrepare(DataDirectory, CoverDirectory);
    }

    private static bool TryPrepare(string dataDirectory, string coverDirectory)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(coverDirectory);

            // 目录存在不代表能写（Program Files），实写一次最保险
            var probe = System.IO.Path.Combine(dataDirectory, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AppSettings LoadSettings()
    {
        lock (_settingsLock)
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (s is not null)
                    {
                        return s;
                    }
                }
            }
            catch
            {
                // 配置损坏则回退默认值
            }
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_settingsLock)
        {
            var tmp = $"{SettingsFile}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(tmp, json);
                File.Move(tmp, SettingsFile, overwrite: true);
            }
            catch
            {
                // 保存失败不致命
            }
            finally
            {
                TryDeleteFile(tmp);
            }
        }
    }

    public List<CachedSeries> LoadCache()
    {
        lock (_cacheLock)
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    var json = File.ReadAllText(CacheFile);
                    return JsonSerializer.Deserialize<List<CachedSeries>>(json, JsonOptions) ?? [];
                }
            }
            catch
            {
                // 缓存损坏则重新扫描
            }
            return [];
        }
    }

    public void SaveCache(IEnumerable<CachedSeries> series)
    {
        var snapshot = series.ToList();
        lock (_cacheLock)
        {
            var tmp = $"{CacheFile}.{Guid.NewGuid():N}.tmp";
            try
            {
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(tmp, json);
                File.Move(tmp, CacheFile, overwrite: true);
            }
            catch
            {
                // ignore
            }
            finally
            {
                TryDeleteFile(tmp);
            }
        }
    }

    /// <summary>丢弃缓存文件（解析规则升级后旧标题不可再用）。</summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            TryDeleteFile(CacheFile);
        }
    }

    /// <summary>封面文件名：以剧集 Key 的哈希命名，避免非法字符。</summary>
    public string CoverPathFor(string seriesKey)
    {
        var hash = Util.Hashing.ShortSha1(seriesKey, 20);
        return System.IO.Path.Combine(CoverDirectory, hash + ".jpg");
    }

    public void ClearCovers()
    {
        try
        {
            if (!Directory.Exists(CoverDirectory)) return;
            foreach (var f in Directory.EnumerateFiles(CoverDirectory, "*.jpg"))
            {
                TryDeleteFile(f);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// 删掉封面目录里「这次启动没用到」的图：剧集被删、文件夹改名之后留下的孤儿封面。
    /// <paramref name="keepPaths"/> 是本次仍在用的封面绝对路径；
    /// 比 <paramref name="minAge"/> 更新的文件一律不动（可能正在生成）。
    /// 返回删掉的张数。
    /// </summary>
    public int PruneUnusedCovers(IEnumerable<string> keepPaths, TimeSpan minAge)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(CoverDirectory)) return 0;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in keepPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { keep.Add(System.IO.Path.GetFullPath(p)); } catch { }
            }

            var cutoff = DateTime.UtcNow - minAge;
            foreach (var file in Directory.EnumerateFiles(CoverDirectory))
            {
                var name = System.IO.Path.GetFileName(file);

                // 正在抽帧的临时文件（xxx.jpg.tmp.jpg）不能碰
                if (name.Contains(".tmp.", StringComparison.OrdinalIgnoreCase)) continue;

                string full;
                try { full = System.IO.Path.GetFullPath(file); } catch { continue; }
                if (keep.Contains(full)) continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(full) > cutoff) continue;
                    File.Delete(full);
                    removed++;
                }
                catch
                {
                    // 单张删不掉无所谓
                }
            }
        }
        catch
        {
            // 清理失败不影响启动
        }
        return removed;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}

public sealed class CachedSeries
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string? CoverPath { get; set; }
    public List<CachedFile> Files { get; set; } = [];
}

public sealed class CachedFile
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string? ShowTitle { get; set; }
    public string? EpisodeTitle { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public int? SpecialNumber { get; set; }
    public bool IsSpecial { get; set; }
    public string? ReleaseGroup { get; set; }
    public long SizeBytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
}
