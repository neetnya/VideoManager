using System.Text;
using System.Text.RegularExpressions;
namespace VideoManager.Core.Util;

/// <summary>「02」排在「10」前面的自然排序。</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    private static readonly Regex ChunkRegex = new(@"(\d+|\D+)", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var a = ChunkRegex.Matches(x);
        var b = ChunkRegex.Matches(y);
        var n = Math.Min(a.Count, b.Count);

        for (var i = 0; i < n; i++)
        {
            var sa = a[i].Value;
            var sb = b[i].Value;
            var na = long.TryParse(sa, out var va);
            var nb = long.TryParse(sb, out var vb);

            int cmp;
            if (na && nb)
            {
                cmp = va.CompareTo(vb);
            }
            else if (na != nb)
            {
                // 数字排在文字前
                cmp = na ? -1 : 1;
            }
            else
            {
                cmp = string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
            }

            if (cmp != 0) return cmp;
        }

        if (a.Count != b.Count) return a.Count.CompareTo(b.Count);
        return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
    }
}
