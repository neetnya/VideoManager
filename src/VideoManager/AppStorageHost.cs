using System.IO;
using VideoManager.Core.Media;
using VideoManager.Core.Models;
using VideoManager.Core.Storage;

namespace VideoManager;

/// <summary>把 Core 的存储 / 封面服务包一层，方便 ViewModel 使用。</summary>
public sealed class AppStorageHost
{
    public AppStorage Storage { get; }
    public CoverService Covers { get; private set; }

    public AppSettings Settings { get; }

    public AppStorageHost()
    {
        Storage = new AppStorage();
        Settings = Storage.LoadSettings();
        Covers = new CoverService(Settings.FfmpegPath);
    }

    /// <summary>重新探测 ffmpeg（用户改了路径之后）。</summary>
    public CoverService RebuildCoverService()
    {
        Covers = new CoverService(Settings.FfmpegPath);
        return Covers;
    }

    public void Save() => Storage.SaveSettings(Settings);
}
