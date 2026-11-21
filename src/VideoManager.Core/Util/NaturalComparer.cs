namespace VideoManager.Core.Util;

/// <summary>「02」排在「10」前面的自然排序（零正则分配）。</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var sx = x.AsSpan();
        var sy = y.AsSpan();
        var ix = 0;
        var iy = 0;

        while (ix < sx.Length && iy < sy.Length)
        {
            var dx = char.IsAsciiDigit(sx[ix]);
            var dy = char.IsAsciiDigit(sy[iy]);

            if (dx && dy)
            {
                var startX = ix;
                while (ix < sx.Length && char.IsAsciiDigit(sx[ix])) ix++;
                var startY = iy;
                while (iy < sy.Length && char.IsAsciiDigit(sy[iy])) iy++;

                var cmp = CompareNumberSpans(sx[startX..ix], sy[startY..iy]);
                if (cmp != 0) return cmp;
            }
            else if (dx != dy)
            {
                // 数字排在文字前
                return dx ? -1 : 1;
            }
            else
            {
                var startX = ix;
                while (ix < sx.Length && !char.IsAsciiDigit(sx[ix])) ix++;
                var startY = iy;
                while (iy < sy.Length && !char.IsAsciiDigit(sy[iy])) iy++;

                var cmp = sx[startX..ix].CompareTo(sy[startY..iy], StringComparison.CurrentCultureIgnoreCase);
                if (cmp != 0) return cmp;
            }
        }

        if (ix != sx.Length || iy != sy.Length)
        {
            return ix == sx.Length ? -1 : 1;
        }

        return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>比较两段纯 ASCII 数字，忽略前导零，不受 long 溢出限制。</summary>
    private static int CompareNumberSpans(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var ta = a.TrimStart('0');
        var tb = b.TrimStart('0');

        if (ta.Length != tb.Length)
        {
            return ta.Length.CompareTo(tb.Length);
        }

        return ta.CompareTo(tb, StringComparison.Ordinal);
    }
}
