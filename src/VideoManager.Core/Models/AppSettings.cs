namespace VideoManager.Core.Models;

/// <summary>持久化配置。</summary>
public sealed class AppSettings
{
    public List<LibraryRoot> Roots { get; set; } = [];

    /// <summary>ffmpeg.exe 路径；空则自动探测。</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>封面取帧位置（视频时长百分比 0-1）。</summary>
    public double CoverFrameRatio { get; set; } = 0.33;

    /// <summary>封面宽度（像素）。</summary>
    public int CoverWidth { get; set; } = 480;

    /// <summary>列表缩略图宽度。</summary>
    public int ThumbWidth { get; set; } = 320;

    /// <summary>是否在导入后自动生成全部封面。</summary>
    public bool AutoCover { get; set; } = true;

    /// <summary>自定义剧集标题（剧集键 → 新标题）。</summary>
    public Dictionary<string, string> TitleOverrides { get; set; } = [];

    /// <summary>自定义封面（剧集键 → 图片路径）。</summary>
    public Dictionary<string, string> CoverOverrides { get; set; } = [];

    /// <summary>已看过的视频（路径 → 上次播放 UTC 时间刻度）。</summary>
    public Dictionary<string, long> Watched { get; set; } = [];

    /// <summary>最近播放过的视频路径（新→旧）。</summary>
    public List<string> Recent { get; set; } = [];

    /// <summary>上次窗口尺寸与位置。</summary>
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 860;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }

    /// <summary>上次扫描时间。</summary>
    public DateTime? LastScanUtc { get; set; }

    /// <summary>卡片大小预设：Small / Medium / Large。</summary>
    public string CardSize { get; set; } = "Medium";
}
