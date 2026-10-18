using System.Diagnostics;
using System.IO;

namespace VideoManager.Core.Media;

/// <summary>用系统默认播放器打开视频。</summary>
public static class PlayerLauncher
{
    public static bool Play(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            // 退回 explorer 打开
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return true;
            }
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                return true;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }
}
