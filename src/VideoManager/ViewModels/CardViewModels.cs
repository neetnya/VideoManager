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
    private bool _hasCover;
    private bool _coverPending;
    private int _coverRevision;

    public SeriesCardViewModel(Series series)
    {
        Series = series;
        Key = series.Key;
        Title = series.Title;
        FolderPath = series.FolderPath;
        RootPath = series.RootPath;
        _coverPath = series.EffectiveCover;
        _hasCover = !string.IsNullOrEmpty(_coverPath) && File.Exists(_coverPath);
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
    public MediaFile? FirstFile { get; private set; }
    public string SearchText { get; private set; }

    /// <summary>最近播放排序位次（越小越新，未播放为 int.MaxValue）。</summary>
    public int RecentRank { get; set; } = int.MaxValue;

    private int _count;
    private string _seasonLabel = "";
    private string _subtitle = "";

    /// <summary>集数。删掉几集之后会变，所以要走通知。</summary>
    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    public string SeasonLabel
    {
        get => _seasonLabel;
        private set => SetProperty(ref _seasonLabel, value);
    }

    /// <summary>副标题：集数 + 所在文件夹名。</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    /// <summary>删掉文件后刷新集数、首集、季标签和副标题。</summary>
    public void UpdateCount(int count)
    {
        Count = count;
        FirstFile = Series.FirstFile;
        SeasonLabel = VideoManager.Core.Library.LibraryScanner.ComputeSeasonLabel(Series.Files);
        Series.SeasonLabel = SeasonLabel;
        Subtitle = BuildSubtitle(Series);
        SearchText = BuildSearchText(Series);
        Raise(nameof(FirstFile), nameof(SearchText));
    }

    public string CoverPath
    {
        get => _coverPath ?? "";
        private set
        {
            if (SetProperty(ref _coverPath, value))
            {
                HasCover = !string.IsNullOrEmpty(_coverPath) && File.Exists(_coverPath);
            }
        }
    }

    /// <summary>是否已有可用封面（避免在每次 UI 绑定读取时做同步 File.Exists 系统调用）。</summary>
    public bool HasCover
    {
        get => _hasCover;
        private set => SetProperty(ref _hasCover, value);
    }

    /// <summary>封面内容版本号（路径不变但重写了新图时递增，触发 AsyncImage 重新加载）。</summary>
    public int CoverRevision
    {
        get => _coverRevision;
        private set => SetProperty(ref _coverRevision, value);
    }

    public bool CoverPending
    {
        get => _coverPending;
        set => SetProperty(ref _coverPending, value);
    }

    public void UpdateCover(string? path, bool? exists = null)
    {
        _coverPath = path;
        _hasCover = exists ?? (!string.IsNullOrEmpty(path) && File.Exists(path));
        _coverRevision++;
        CoverPending = false;
        Raise(nameof(CoverPath), nameof(HasCover), nameof(CoverRevision));
    }

    /// <summary>路径没变但文件内容变了（重抓/换图后）时，强制绑定重新读一次图。</summary>
    public void RefreshCover()
    {
        _hasCover = !string.IsNullOrEmpty(_coverPath) && File.Exists(_coverPath);
        _coverRevision++;
        Raise(nameof(CoverPath), nameof(HasCover), nameof(CoverRevision));
    }

    public void UpdateTitle(string title)
    {
        Title = title;
        Series.Title = title;
        Subtitle = BuildSubtitle(Series);
        SearchText = BuildSearchText(Series);
        Raise(nameof(Title), nameof(SearchText));
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
