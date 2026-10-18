namespace VideoManager.Core.Models;

/// <summary>一条视频记录（扫描得到，或来自缓存）。</summary>
public sealed class MediaFile
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string FolderPath { get; init; }
    public required string RootPath { get; init; }

    /// <summary>从文件名解析出的「剧名」（可能为空，此时剧名取文件夹名）。</summary>
    public string? ShowTitle { get; init; }

    /// <summary>从文件名解析出的「本集标题」。</summary>
    public string? EpisodeTitle { get; init; }

    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? SpecialNumber { get; init; }
    public bool IsSpecial { get; init; }
    public string? ReleaseGroup { get; init; }

    public long SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }

    /// <summary>显示用标题：优先本集标题，其次从文件名里剥出来的干净名字，最后「第 N 集」。</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(EpisodeTitle))
            {
                return EpisodeTitle!;
            }
            if (IsSpecial && SpecialNumber is > 0)
            {
                return $"特别篇 {SpecialNumber}";
            }

            // 文件名里的 [字幕组][分辨率][编码] 之类对用户没意义。剥掉标签、
            // 再去掉「剧名前缀 + 集数尾巴」，剩下的才是真正值得显示的东西。
            var cleaned = StripTags(Path.GetFileNameWithoutExtension(FileName));
            cleaned = TrimPrefix(cleaned, ShowTitle);
            cleaned = OnlyEpisodeCode.IsMatch(cleaned) ? string.Empty : cleaned;

            if (cleaned.Length >= 2)
            {
                return cleaned;
            }
            if (Episode is > 0)
            {
                return $"第 {Episode} 集";
            }
            return Path.GetFileNameWithoutExtension(FileName);
        }
    }

    /// <summary>S02E59 / 01 / 01v2 / E03 这种纯集数记号。</summary>
    private static readonly System.Text.RegularExpressions.Regex OnlyEpisodeCode = new(
        @"^[Ss]?\d{1,3}(?:[Ee][Pp]?)?\d{0,4}(?:[vV]\d+)?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>去掉 [] 与 () 包裹的发布标签，压掉多余分隔符。</summary>
    private static string StripTags(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        var depth = 0;
        foreach (var c in name)
        {
            if (c is '[' or '【')
            {
                depth++;
                continue;
            }
            if (c is ']' or '】')
            {
                if (depth > 0) depth--;
                continue;
            }
            if (depth > 0) continue;
            sb.Append(c);
        }

        var parts = sb.ToString().Split([' ', '_', '.', '-', '·'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).Trim();
    }

    /// <summary>
    /// 如果 <paramref name="text"/> 以剧名开头，就把这段前缀切掉。
    /// 比较时忽略空白与标点（！/!/ ★ /,/, 之类），避免因为全角半角差异认不出来。
    /// </summary>
    private static string TrimPrefix(string text, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || text.Length == 0)
        {
            return text;
        }

        var p = 0;
        var t = 0;
        for (; t < text.Length; t++)
        {
            var a = Normalize(text[t]);
            if (a == '\0')
            {
                continue;
            }
            while (p < prefix.Length && Normalize(prefix[p]) == '\0')
            {
                p++;
            }
            if (p >= prefix.Length)
            {
                break;
            }
            if (a != Normalize(prefix[p]))
            {
                return text;
            }
            p++;
        }

        while (p < prefix.Length && Normalize(prefix[p]) == '\0')
        {
            p++;
        }
        if (p < prefix.Length)
        {
            return text;
        }

        var rest = text[t..].Trim(' ', '-', '_', '·', '—', '–', ':', '：');
        return rest;
    }

    private static char Normalize(char c)
    {
        if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
        {
            return '\0';
        }
        return char.ToLowerInvariant(c);
    }

    /// <summary>集数标签：E01 / SP1 / 无。</summary>
    public string EpisodeLabel
    {
        get
        {
            if (IsSpecial)
            {
                return SpecialNumber is > 0 ? $"SP{SpecialNumber}" : "SP";
            }
            return Episode is > 0 ? $"E{Episode:00}" : "—";
        }
    }

    /// <summary>大小（人类可读）。</summary>
    public string SizeText
    {
        get
        {
            var mb = SizeBytes / 1024.0 / 1024.0;
            return mb >= 1024 ? $"{mb / 1024:0.##} GB" : $"{mb:0.#} MB";
        }
    }

    /// <summary>季集标识，例如 S2E05。</summary>
    public string SeasonEpisodeLabel => Season is > 0
        ? $"S{Season} {EpisodeLabel}"
        : EpisodeLabel;
}
