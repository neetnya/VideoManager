using System.Text.RegularExpressions;

namespace VideoManager.Core.Parsing;

/// <summary>文件名解析结果。</summary>
public sealed record ParsedName
{
    public string? ShowTitle { get; init; }
    public string? EpisodeTitle { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? SpecialNumber { get; init; }
    public bool IsSpecial { get; init; }
    public string? ReleaseGroup { get; init; }
    public double Confidence { get; init; }

    public bool HasEpisode => Episode is > 0;
}

/// <summary>
/// 从「文件名 / 文件夹名」里解析出剧名、季、集、本集标题。
/// 支持：S01EP10 / S02E59 / [组名][标题][001][1080p] / 标题 - 01 /
///       27 我成为名侦探了 / 1.mp4 / 第3话 / 【组】标题[36][试炼的金牌]
/// </summary>
public static class NameParser
{
    private static readonly Regex BracketRegex = new(@"\[([^\[\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex ParenRegex = new(@"\(([^()]*)\)", RegexOptions.Compiled);

    private static readonly Regex SeasonEpisodeRegex = new(
        @"(?<![A-Za-z0-9])(?:S|Season\s*)(?<s>\d{1,2})[\s._\-]*(?:EP|E|Episode\s*)[\s._\-]*(?<e>\d{1,4})(?:[vV]\d)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CnSeasonEpisodeRegex = new(
        @"第\s*(?<s>\d{1,2})\s*季[\s._\-]*(?:第\s*)?(?<e>\d{1,4})\s*[集话話期]",
        RegexOptions.Compiled);

    private static readonly Regex CnEpisodeRegex = new(
        @"第\s*(?<e>\d{1,4})\s*[集话話期]",
        RegexOptions.Compiled);

    private static readonly Regex DashEpisodeRegex = new(
        @"[\s._]*[-–—~～][\s._]*(?<e>\d{1,4})(?:[\s._]*[vV]\d)?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TrailingEpisodeRegex = new(
        @"(?<=\s|\])(?<e>\d{1,4})(?:[\s._]*[vV]\d)?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex LeadingEpisodeRegex = new(
        @"^(?<e>\d{1,4})(?![\d.])\s*[.、,·]?\s+",
        RegexOptions.Compiled);

    private static readonly Regex SpecialMarkerRegex = new(
        @"(?:^|[\s._\-\[\(])(?:SP|SP\s*(?<n>\d{1,2})|特別篇|特别篇|特别编|OVA|OAD|劇場版|剧场版|Movie|映画)(?=$|[\s._\-\]\)])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SeasonFolderRegex = new(
        @"^(?:S|Season\s*|第\s*)(?<s>\d{1,2})\s*(?:季|st|nd|rd|th)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VersionSuffixRegex = new(@"[vV]\d+$", RegexOptions.Compiled);

    private static readonly Regex SeparatorRunRegex = new(@"[\s._·•]+", RegexOptions.Compiled);

    private static readonly Regex EdgeJunkRegex = new(@"^[\s\-–—_.,、:：/\\|~～]+|[\s\-–—_.,、:：/\\|~～]+$", RegexOptions.Compiled);

    private static readonly Regex CjkRegex = new(@"[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uac00-\ud7af\uff01-\uff60]", RegexOptions.Compiled);

    // 常见「标签」词：出现即认为这个方括号不是标题
    private static readonly HashSet<string> TagTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "web", "webdl", "webrip", "web-dl", "bluray", "blu-ray", "bdrip", "bd", "dvdrip", "dvd", "tvrip", "hdtv",
        "remux", "hevc", "avc", "av1", "vp9", "x264", "x265", "h264", "h265", "10bit", "8bit", "hi10p", "ma10p",
        "aac", "ac3", "eac3", "dts", "flac", "opus", "mp3", "truehd", "atmos", "ass", "srt", "sub", "subs",
        "chs", "cht", "gb", "big5", "jpn", "eng", "chi", "jpsc", "jpsc&chs", "sc", "tc", "baha", "cr", "uncensored",
        "uncencored", "censored", "hdr", "hdr10", "dv", "mkv", "mp4", "hdtc", "tc", "cam", "menu", "sp", "ova",
        "oad", "pv", "cm", "nced", "ncop", "end", "op", "ed", "v2", "v3", "v4", "fin", "complete", "batch",
        "简", "繁", "简体", "繁体", "简繁", "字幕", "内嵌", "外挂", "双语", "中日双语", "简日双语", "繁体中文",
    };

    private static readonly Regex ResolutionRegex = new(@"^\d{3,4}[pi]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AudioChannelRegex = new(@"^\d\.\d$", RegexOptions.Compiled);
    private static readonly Regex VersionOnlyRegex = new(@"^[vV]\d+$", RegexOptions.Compiled);

    /// <summary>是否是「季」目录名，例如 S2 / Season 3 / 第二季。</summary>
    public static int? ParseSeasonFolder(string folderName)
    {
        var n = Normalize(folderName).Trim();
        var m = SeasonFolderRegex.Match(n);
        if (m.Success && int.TryParse(m.Groups["s"].Value, out var s) && s is > 0 and < 100)
        {
            return s;
        }
        return null;
    }

    public static bool IsSeasonFolder(string folderName) => ParseSeasonFolder(folderName) is not null;

    public sealed record BracketInfo(string Text, bool IsTag, bool IsNumber, int? Number, int Index);

    public static ParsedName Parse(string fileName)
    {
        var raw = System.IO.Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedName();
        }

        var norm = Normalize(raw);

        // 1) 拆方括号 / 圆括号
        var brackets = new List<BracketInfo>();
        var work = norm.ToCharArray();

        foreach (Match m in BracketRegex.Matches(norm))
        {
            var text = m.Groups[1].Value.Trim();
            var info = Classify(text, m.Index);
            brackets.Add(info);
            if (!(info.IsTag || info.IsNumber) && info.Text.Length > 0 && m.Index > 0 && IsLikelyTitleBracket(info.Text))
            {
                // 标题型方括号也一并从主串移除，稍后单独使用
            }
            for (var i = m.Index; i < m.Index + m.Length; i++)
            {
                work[i] = ' ';
            }
        }

        // 圆括号：只移除「标签型」或「纯数字型」，其余保留（可能是标题的一部分）
        foreach (Match m in ParenRegex.Matches(norm))
        {
            var text = m.Groups[1].Value.Trim();
            var info = Classify(text, m.Index);
            if (!(info.IsTag || info.IsNumber))
            {
                continue;
            }
            brackets.Add(info);
            for (var i = m.Index; i < m.Index + m.Length; i++)
            {
                work[i] = ' ';
            }
        }

        var workText = new string(work);
        workText = CollapseJunk(workText);

        // 2) 公开发布组：位于开头的第一个非数字、非标签方括号
        string? releaseGroup = null;
        var first = brackets.FirstOrDefault();
        if (first is not null && first.Index == 0 && !first.IsNumber && !first.IsTag && first.Text.Length is > 0 and <= 32)
        {
            releaseGroup = CleanTitle(first.Text);
        }

        // 3) 优先从主串找 SxxExx
        int? season = null;
        int? episode = null;
        string? showTitle = null;
        string? episodeTitle = null;

        // 去掉集数标记后剩下的文本（剧名候选）；null 表示还没找到集数标记
        string? residual = null;

        var se = SeasonEpisodeRegex.Match(workText);
        if (se.Success)
        {
            season = int.Parse(se.Groups["s"].Value);
            episode = int.Parse(se.Groups["e"].Value);
            var before = workText[..se.Index];
            var after = workText[(se.Index + se.Length)..];
            SplitShowEpisode(before, after, ref showTitle, ref episodeTitle);
            residual = before;
        }
        else
        {
            var cn = CnSeasonEpisodeRegex.Match(workText);
            if (cn.Success)
            {
                season = int.Parse(cn.Groups["s"].Value);
                episode = int.Parse(cn.Groups["e"].Value);
                var before = workText[..cn.Index];
                var after = workText[(cn.Index + cn.Length)..];
                SplitShowEpisode(before, after, ref showTitle, ref episodeTitle);
                residual = before;
            }
            else
            {
                var cne = CnEpisodeRegex.Match(workText);
                if (cne.Success)
                {
                    episode = int.Parse(cne.Groups["e"].Value);
                    SplitShowEpisode(workText[..cne.Index], workText[(cne.Index + cne.Length)..], ref showTitle, ref episodeTitle);
                    residual = workText[..cne.Index];
                }
            }
        }

        // 4) 主串里没有集数：从方括号里找纯数字
        if (episode is null)
        {
            var numBracket = brackets.FirstOrDefault(b => b.IsNumber && b.Number is > 0 and <= 999);
            if (numBracket is not null)
            {
                episode = numBracket.Number;

                // 集数方括号之后、看起来像文字的方括号 → 本集标题
                var afterBrackets = brackets.Where(b => b.Index > numBracket.Index && !b.IsNumber && !b.IsTag)
                    .Select(b => CleanTitle(b.Text))
                    .Where(t => t.Length > 0)
                    .ToList();
                if (afterBrackets.Count > 0)
                {
                    episodeTitle = afterBrackets[0];
                }

                // 集数方括号之前、像文字的方括号 → 剧名
                var beforeBrackets = brackets.Where(b => b.Index < numBracket.Index && !b.IsNumber && !b.IsTag)
                    .Select(b => CleanTitle(b.Text))
                    .Where(t => t.Length > 0 && !string.Equals(t, releaseGroup, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (workText.Length >= 2)
                {
                    showTitle = CleanTitle(workText);
                    residual = workText;
                }
                else
                {
                    showTitle = beforeBrackets.LastOrDefault();
                    residual = string.Join(" ", beforeBrackets);
                }
            }
        }

        // 5) 主串里还没有：尝试破折号集数、末尾孤立数字
        if (episode is null)
        {
            var dash = DashEpisodeRegex.Match(workText);
            if (dash.Success && TryEpisode(dash.Groups["e"].Value, out var de))
            {
                episode = de;
                SplitShowEpisode(workText[..dash.Index], workText[(dash.Index + dash.Length)..], ref showTitle, ref episodeTitle);
            }
        }

        if (episode is null)
        {
            var tail = TrailingEpisodeRegex.Match(workText);
            if (tail.Success && TryEpisode(tail.Groups["e"].Value, out var te))
            {
                episode = te;
                SplitShowEpisode(workText[..tail.Index], workText[(tail.Index + tail.Length)..], ref showTitle, ref episodeTitle);
            }
        }

        if (episode is null)
        {
            var lead = LeadingEpisodeRegex.Match(workText);
            if (lead.Success && TryEpisode(lead.Groups["e"].Value, out var le))
            {
                episode = le;
                var after = workText[(lead.Index + lead.Length)..];
                episodeTitle = CleanTitle(after);
            }
        }

        // 6) 整个名字就是一个数字
        if (episode is null && int.TryParse(workText.Trim(), out var whole) && whole is > 0 and <= 999)
        {
            episode = whole;
        }

        // 7) 特别篇标记
        int? specialNumber = null;
        var isSpecial = false;
        var spSource = norm;
        var sp = SpecialMarkerRegex.Match(spSource);
        if (sp.Success)
        {
            isSpecial = true;
            if (sp.Groups["n"].Success && int.TryParse(sp.Groups["n"].Value, out var spn))
            {
                specialNumber = spn;
            }
        }
        // 方括号里的「特别篇」也算
        if (!isSpecial && brackets.Any(b => b.Text is "特别篇" or "特別篇" or "SP" or "OVA"))
        {
            isSpecial = true;
        }

        // 8) 兜底：主串剩余文本当作剧名（已扣掉集数标记的部分优先）
        if (showTitle is null)
        {
            var tail = residual is not null ? residual : workText;
            tail = StripNumberRuns(tail);
            showTitle = tail.Length >= 2 ? CleanTitle(tail) : null;
        }
        episodeTitle = CleanEpisodeTitle(episodeTitle);

        if (showTitle is { Length: 0 })
        {
            showTitle = null;
        }

        // 「TV-」「SP1-」这类标记不要留在标题里
        episodeTitle = StripTypeMarker(episodeTitle);
        showTitle = StripTypeMarker(showTitle);

        var confidence = 0.4;
        if (episode is not null) confidence += 0.35;
        if (season is not null) confidence += 0.1;
        if (showTitle is not null) confidence += 0.1;
        if (releaseGroup is not null) confidence += 0.05;

        return new ParsedName
        {
            ShowTitle = showTitle,
            EpisodeTitle = episodeTitle,
            Season = season,
            Episode = episode,
            SpecialNumber = specialNumber,
            IsSpecial = isSpecial,
            ReleaseGroup = releaseGroup,
            Confidence = Math.Min(confidence, 1.0),
        };
    }

    private static void SplitShowEpisode(string before, string after, ref string? showTitle, ref string? episodeTitle)
    {
        var b = CleanTitle(before);
        var a = CleanTitle(after);
        if (a.Length > 0)
        {
            episodeTitle = a;
            if (b.Length >= 2)
            {
                showTitle = b;
            }
        }
        else if (b.Length >= 2)
        {
            showTitle = b;
        }
    }

    private static bool TryEpisode(string s, out int value)
    {
        value = 0;
        if (!int.TryParse(s, out var v))
        {
            return false;
        }
        if (v is <= 0 or > 999)
        {
            return false;
        }
        value = v;
        return true;
    }

    private static BracketInfo Classify(string text, int index)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return new BracketInfo(t, true, false, null, index);
        }

        if (VersionOnlyRegex.IsMatch(t) && t.Length <= 4)
        {
            return new BracketInfo(t, true, false, null, index);
        }

        // 01 / 001 / 01v2 / 12.5 这类：可能是集数
        var num = VersionSuffixRegex.Replace(t, "");
        if (int.TryParse(num, out var n) && n is > 0 and <= 999 && (num == t || VersionSuffixRegex.IsMatch(t)))
        {
            var looksLikeYear = n is >= 1900 and <= 2100;
            if (!looksLikeYear)
            {
                return new BracketInfo(t, false, true, n, index);
            }
        }

        return new BracketInfo(t, IsTagText(t), false, null, index);
    }

    private static bool IsLikelyTitleBracket(string text)
    {
        var t = text.Trim();
        return t.Length >= 2 && !IsTagText(t);
    }

    /// <summary>判断一段方括号内容是不是「压制信息」而不是标题。</summary>
    public static bool IsTagText(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return true;
        }

        if (ResolutionRegex.IsMatch(t) || AudioChannelRegex.IsMatch(t))
        {
            return true;
        }

        var tokens = t.Split([' ', '&', '+', '-', ',', '/', '\\', '|', '~', '_', '.', '·'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        var tagCount = 0;
        foreach (var token in tokens)
        {
            var tk = token.Trim().ToLowerInvariant();
            if (TagTokens.Contains(tk) || ResolutionRegex.IsMatch(tk) || VersionOnlyRegex.IsMatch(tk) || AudioChannelRegex.IsMatch(tk))
            {
                tagCount++;
            }
        }

        return tagCount >= Math.Max(1, (int)Math.Ceiling(tokens.Length * 0.6));
    }

    private static string? CleanEpisodeTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        var t = StripTypeMarker(s);
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static string? StripTypeMarker(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return s;
        }
        var t = Regex.Replace(s.Trim(), @"^(?:TV|SP\d*|S\d+|EP?\d+|剧场版|劇場版)[\s._\-–—:：]+", "", RegexOptions.IgnoreCase);
        t = EdgeJunkRegex.Replace(t, "");
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    /// <summary>把一堆分隔符乱七八糟的文本收拾成清爽的标题。</summary>
    public static string CleanTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        var t = s.Trim();

        // 去掉残留的标签词（例如标题尾部粘着的 1080p）
        t = Regex.Replace(t, @"\s*[\[\(（【][^\]\)）】]*[\]\)）】]", " ");
        t = Regex.Replace(t, @"\s*\b\d{3,4}[pPiI]\b", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*\b(?:x26[45]|h26[45]|hevc|avc|aac|web-?dl|webrip|bluray|bdrip|flac|opus|ma10p|hi10p)\b", " ", RegexOptions.IgnoreCase);

        t = SeparatorRunRegex.Replace(t, " ");
        t = EdgeJunkRegex.Replace(t, "");
        t = t.Trim();

        if (t.Length == 0)
        {
            return "";
        }

        // 纯数字 / 纯符号的不算标题
        if (t.All(c => !char.IsLetter(c)))
        {
            return "";
        }

        return t;
    }

    /// <summary>全角 → 半角，方括号统一，便于正则处理。</summary>
    public static string Normalize(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '\uFF01' and <= '\uFF5E')
            {
                chars[i] = (char)(c - 0xFEE0);
            }
            else if (c == '\u3000')
            {
                chars[i] = ' ';
            }
        }

        var t = new string(chars);
        t = t.Replace('【', '[').Replace('】', ']');
        t = t.Replace('（', '(').Replace('）', ')');
        t = t.Replace('［', '[').Replace('］', ']');
        return t;
    }

    private static string CollapseJunk(string s)
    {
        var t = SeparatorRunRegex.Replace(s, " ").Trim();
        return t;
    }

    private static readonly Regex NumberRunRegex = new(@"(?:^|\s)\d{1,4}(?:[vV]\d+)?(?=\s|$)", RegexOptions.Compiled);

    /// <summary>去掉正文里孤立的集数数字。</summary>
    private static string StripNumberRuns(string s) => NumberRunRegex.Replace(s, " ");

    public static bool ContainsCjk(string s) => !string.IsNullOrEmpty(s) && CjkRegex.IsMatch(s);
}
