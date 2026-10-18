using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoManager.Common;

/// <summary>路径 → 缩略图（带缓存、按宽度解码，避免大图占内存）。</summary>
public sealed class ImagePathToSourceConverter : IValueConverter
{
    public static readonly ImagePathToSourceConverter Instance = new();

    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
        {
            return null;
        }

        var width = 420;
        if (parameter is string ps && int.TryParse(ps, out var w))
        {
            width = w;
        }

        var key = path + "|" + width;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapImage? bmp = null;
        try
        {
            if (File.Exists(path))
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = width;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
            }
        }
        catch
        {
            bmp = null;
        }

        Cache[key] = bmp;
        return bmp;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static void Clear() => Cache.Clear();

    /// <summary>让某个文件的所有缓存尺寸失效（封面被重写后必须调用，否则界面还是旧图）。</summary>
    public static void Invalidate(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var prefix = path + "|";
        foreach (var key in Cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not bool b || !b;
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public static readonly NullOrEmptyToVisibilityConverter Instance = new();
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string s && s.Length == 0);
        var visible = Invert ? empty : !empty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>取字符串首字，用于没有封面时的占位。</summary>
public sealed class InitialsConverter : IValueConverter
{
    public static readonly InitialsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "?";
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) return c.ToString().ToUpperInvariant();
        }
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串哈希 → 稳定配色（无封面占位的底色）。</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x1B, 0x3A, 0x5C), Color.FromRgb(0x2A, 0x2C, 0x5E), Color.FromRgb(0x1E, 0x4A, 0x52),
        Color.FromRgb(0x3A, 0x2A, 0x50), Color.FromRgb(0x18, 0x39, 0x3F), Color.FromRgb(0x33, 0x2C, 0x42),
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var h = 0;
        foreach (var c in s) h = (h * 31 + c) & 0x7FFFFFFF;
        var brush = new SolidColorBrush(Palette[h % Palette.Length]);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
