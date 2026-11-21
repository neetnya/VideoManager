using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoManager.Common;

/// <summary>
/// 有界 LRU 缩略图缓存（线程安全），防止大媒体库内存无限增长。
/// </summary>
public static class ImageCache
{
    private const int MaxEntries = 280;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> Map =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, BitmapImage Image)> Order = new();

    public static bool TryGet(string path, int width, out BitmapImage? image)
    {
        var key = MakeKey(path, width);
        lock (Gate)
        {
            if (Map.TryGetValue(key, out var node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null;
        return false;
    }

    public static void Put(string path, int width, BitmapImage image)
    {
        var key = MakeKey(path, width);
        lock (Gate)
        {
            if (Map.TryGetValue(key, out var existing))
            {
                Order.Remove(existing);
                existing.Value = (key, image);
                Order.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<(string, BitmapImage)>((key, image));
            Order.AddFirst(node);
            Map[key] = node;

            while (Map.Count > MaxEntries && Order.Last is { } oldest)
            {
                Order.RemoveLast();
                Map.Remove(oldest.Value.Key);
            }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Map.Clear();
            Order.Clear();
        }
    }

    public static void Invalidate(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var prefix = path + "|";
        lock (Gate)
        {
            var toRemove = new List<string>();
            foreach (var key in Map.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                if (Map.Remove(key, out var node))
                {
                    Order.Remove(node);
                }
            }
        }
    }

    public static byte[]? TryReadBytes(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream((int)Math.Min(fs.Length, int.MaxValue));
            fs.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? DecodeFromBytes(byte[]? bytes, int width)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = width;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? LoadFromUri(string path, int width)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = width;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? LoadFrozen(string path, int width)
        => DecodeFromBytes(TryReadBytes(path), width) ?? LoadFromUri(path, width);

    private static string MakeKey(string path, int width) => $"{path}|{width}";
}

/// <summary>
/// 异步图片加载附加属性：命中 LRU 缓存时同步零闪烁显示，未命中时在后台线程解码并 Freeze，
/// 避免滚动卡片网格时阻塞 UI 线程。
/// </summary>
public static class AsyncImage
{
    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.RegisterAttached(
            "SourcePath",
            typeof(string),
            typeof(AsyncImage),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty DecodeWidthProperty =
        DependencyProperty.RegisterAttached(
            "DecodeWidth",
            typeof(int),
            typeof(AsyncImage),
            new PropertyMetadata(420, OnSourceChanged));

    public static readonly DependencyProperty RevisionProperty =
        DependencyProperty.RegisterAttached(
            "Revision",
            typeof(int),
            typeof(AsyncImage),
            new PropertyMetadata(0, OnSourceChanged));

    private static readonly DependencyProperty LoadVersionProperty =
        DependencyProperty.RegisterAttached(
            "LoadVersion",
            typeof(int),
            typeof(AsyncImage),
            new PropertyMetadata(0));

    public static string? GetSourcePath(DependencyObject obj) => (string?)obj.GetValue(SourcePathProperty);
    public static void SetSourcePath(DependencyObject obj, string? value) => obj.SetValue(SourcePathProperty, value);

    public static int GetDecodeWidth(DependencyObject obj) => (int)obj.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject obj, int value) => obj.SetValue(DecodeWidthProperty, value);

    public static int GetRevision(DependencyObject obj) => (int)obj.GetValue(RevisionProperty);
    public static void SetRevision(DependencyObject obj, int value) => obj.SetValue(RevisionProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image img) return;

        var version = (int)img.GetValue(LoadVersionProperty) + 1;
        img.SetValue(LoadVersionProperty, version);

        var path = GetSourcePath(img);
        if (string.IsNullOrWhiteSpace(path))
        {
            img.Source = null;
            return;
        }

        var width = Math.Max(64, GetDecodeWidth(img));
        if (ImageCache.TryGet(path, width, out var cached) && cached is not null)
        {
            img.Source = cached;
            return;
        }

        img.Source = null;

        _ = LoadAsync(img, path, width, version);
    }

    private static async Task LoadAsync(Image img, string path, int width, int expectedVersion)
    {
        // 磁盘读取放到后台线程，BitmapImage 解码留在 UI 线程（STA），避免跨线程 WIC 解码失败。
        var bytes = await Task.Run(() => ImageCache.TryReadBytes(path));

        await img.Dispatcher.InvokeAsync(() =>
        {
            if ((int)img.GetValue(LoadVersionProperty) != expectedVersion)
            {
                return;
            }

            var bmp = ImageCache.DecodeFromBytes(bytes, width) ?? ImageCache.LoadFromUri(path, width);
            if (bmp is not null)
            {
                ImageCache.Put(path, width, bmp);
            }
            img.Source = bmp;
        });
    }
}

/// <summary>路径 → 缩略图（共享有界 LRU 缓存，兼容 XAML 绑定）。</summary>
public sealed class ImagePathToSourceConverter : IValueConverter
{
    public static readonly ImagePathToSourceConverter Instance = new();

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

        if (ImageCache.TryGet(path, width, out var cached) && cached is not null)
        {
            return cached;
        }

        var bmp = ImageCache.LoadFrozen(path, width);
        if (bmp is not null)
        {
            ImageCache.Put(path, width, bmp);
        }
        return bmp;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static void Clear() => ImageCache.Clear();

    /// <summary>让某个文件的所有缓存尺寸失效（封面被重写后必须调用，否则界面还是旧图）。</summary>
    public static void Invalidate(string path) => ImageCache.Invalidate(path);
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

/// <summary>字符串哈希 → 稳定配色（复用预冻结画刷，零重复分配）。</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    private static readonly SolidColorBrush[] Brushes = CreateBrushes();

    private static SolidColorBrush[] CreateBrushes()
    {
        Color[] palette =
        [
            Color.FromRgb(0x1B, 0x3A, 0x5C), Color.FromRgb(0x2A, 0x2C, 0x5E), Color.FromRgb(0x1E, 0x4A, 0x52),
            Color.FromRgb(0x3A, 0x2A, 0x50), Color.FromRgb(0x18, 0x39, 0x3F), Color.FromRgb(0x33, 0x2C, 0x42),
        ];

        var result = new SolidColorBrush[palette.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            var b = new SolidColorBrush(palette[i]);
            b.Freeze();
            result[i] = b;
        }
        return result;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        var h = 0;
        foreach (var c in s) h = (h * 31 + c) & 0x7FFFFFFF;
        return Brushes[h % Brushes.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
