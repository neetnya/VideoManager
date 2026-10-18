using System.IO;
using VideoManager.Common;
using VideoManager.Core.Models;

namespace VideoManager.ViewModels;

/// <summary>卡片尺寸预设。</summary>
public enum CardSizePreset
{
    Small,
    Medium,
    Large,
}

/// <summary>主界面的导航分区。</summary>
public enum NavSection
{
    All,
    Recent,
    Roots,
    Settings,
}

/// <summary>剧集卡片（列表 / 网格）的视图模型。</summary>
public sealed class SeriesCardViewModel : ObservableObject
{
    private string? _coverPath;
    private bool _coverPending;

    public SeriesCardViewModel(Series series)
    {
        Series = series;
        Key = series.Key;
        Title = series.Title;
        FolderPath = series.FolderPath;
        RootPath = series.RootPath;
        _coverPath = series.EffectiveCover;
        Count = series.Files.Count;
        SeasonLabel = series.SeasonLabel;
        FirstFile = series.FirstFile;
        SearchText = BuildSearchText(series);
        Subtitle = BuildSubtitle(series);
    }

    public Series Series { get; }
    public string Key { get; }
    public string Title { get; private set; }
    public string FolderPath { get; }
    public string RootPath { get; }
    public int Count { get; }
    public string SeasonLabel { get; }
    public MediaFile? FirstFile { get; }
    public string SearchText { get; }

    /// <summary>副标题：集数 + 所在文件夹名。</summary>
    public string Subtitle { get; }

    public string CoverPath
    {
        get => _coverPath ?? "";
        private set
        {
            if (SetProperty(ref _coverPath, value))
            {
                OnPropertyChanged(nameof(HasCover));
            }
        }
    }

    public bool HasCover => !string.IsNullOrEmpty(_coverPath) && File.Exists(_coverPath);

    public bool CoverPending
    {
        get => _coverPending;
        set => SetProperty(ref _coverPending, value);
    }

    public void UpdateCover(string? path)
    {
        _coverPath = path;
        CoverPending = false;
        Raise(nameof(CoverPath), nameof(HasCover));
    }

    /// <summary>路径没变但文件内容变了（重抓/换图后）时，强制绑定重新读一次图。</summary>
    public void RefreshCover() => Raise(nameof(CoverPath), nameof(HasCover));

    public void UpdateTitle(string title)
    {
        Title = title;
        Raise(nameof(Title));
    }

    private static string SafeFolderName(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(name) ? path : name;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// 副标题：集数 + 位置。文件夹名和剧名重复时这个位置没有信息量，
    /// 散装文件（文件夹就是导入的根）也一样，这两种情况只显示集数。
    /// </summary>
    private static string BuildSubtitle(Series series)
    {
        var where = SafeFolderName(series.FolderPath);

        // 散装文件：文件夹就是导入的根目录
        if (string.Equals(
                series.FolderPath.TrimEnd('\\', '/'),
                series.RootPath.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{series.Files.Count} 集";
        }

        // 文件夹名和剧名只是标点/全半角/繁简差异（或完全一致）时，位置等于剧名
        where = Core.Util.ChineseConverter.ToSimplified(where);
        if (Squash(where) == Squash(series.Title))
        {
            return $"{series.Files.Count} 集";
        }

        return $"{series.Files.Count} 集 · {where}";
    }

    /// <summary>去掉空白与标点、统一小写，用来比对「文件夹名是不是就是剧名」。</summary>
    private static string Squash(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                continue;
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string BuildSearchText(Series series)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(series.Title).Append(' ').Append(series.FolderPath).Append(' ');
        foreach (var f in series.Files)
        {
            sb.Append(f.FileName).Append(' ');
        }
        if (series.Title != series.OriginalTitle)
        {
            sb.Append(series.OriginalTitle).Append(' ');
        }
        return sb.ToString().ToLowerInvariant();
    }
}

/// <summary>剧集详情里的单集条目。</summary>
public sealed class EpisodeItemViewModel : ObservableObject
{
    private bool _watched;

    public EpisodeItemViewModel(MediaFile file, bool watched)
    {
        File = file;
        _watched = watched;
    }

    public MediaFile File { get; }
    public string FullPath => File.FullPath;
    public string DisplayName => File.DisplayName;
    public string EpisodeLabel => File.EpisodeLabel;
    public string SeasonEpisodeLabel => File.SeasonEpisodeLabel;
    public string SizeText => File.SizeText;
    public string FileName => File.FileName;
    public string FolderPath => File.FolderPath;

    public bool Watched
    {
        get => _watched;
        set
        {
            if (SetProperty(ref _watched, value))
            {
                OnPropertyChanged(nameof(WatchedMark));
            }
        }
    }

    public string WatchedMark => _watched ? "✓" : "";
}

/// <summary>媒体库根目录条目（设置页）。</summary>
public sealed class RootItemViewModel : ObservableObject
{
    private bool _enabled;
    private int _count;
    private string _status = "";

    public RootItemViewModel(LibraryRoot root, int count)
    {
        Root = root;
        _enabled = root.Enabled;
        _count = count;
    }

    public LibraryRoot Root { get; }
    public string Path => Root.Path;
    public string Name => Root.Name;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                Root.Enabled = value;
                Toggled?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => _count > 0 ? $"{_count} 部" : "—";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public event EventHandler? Toggled;
}
