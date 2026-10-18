using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoManager.Core.Media;

/// <summary>调用 ffmpeg / ffprobe 抽取封面帧。</summary>
public sealed class CoverService
{
    private readonly object _gate = new();

    public string? FfmpegPath { get; private set; }
    public string? FfprobePath { get; private set; }

    public CoverService(string? configuredFfmpeg = null)
    {
        var ff = Locate(configuredFfmpeg, "ffmpeg.exe");
        var fp = ff is null ? null : Locate(Path.Combine(Path.GetDirectoryName(ff)!, "ffprobe.exe"), "ffprobe.exe");
        FfmpegPath = ff;
        FfprobePath = fp ?? (ff is null ? null : Locate("ffprobe.exe", Path.Combine(Path.GetDirectoryName(ff)!, "ffprobe.exe")));
    }

    public bool Available => FfmpegPath is not null;

    public static string? Locate(string? configured, string exeName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var p = configured!.Trim('"');
            if (File.Exists(p))
            {
                return p;
            }
            if (Directory.Exists(p))
            {
                var candidate = Path.Combine(p, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // PATH
        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // 忽略非法路径项
            }
        }

        foreach (var dir in CommonFolders())
        {
            try
            {
                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static IEnumerable<string> CommonFolders()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        yield return @"D:\Tools\ffmpeg";
        yield return @"C:\ffmpeg\bin";
        yield return @"D:\ffmpeg\bin";
        yield return @"C:\Program Files\ffmpeg\bin";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg");
    }

    /// <summary>取视频时长（秒）。失败返回 null。</summary>
    public double? ProbeDuration(string videoPath)
    {
        if (FfprobePath is null) return null;
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"";
        var output = Run(FfprobePath, args, TimeSpan.FromSeconds(20));
        if (output is null) return null;
        var text = output.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 1)
        {
            return d;
        }
        return null;
    }

    /// <summary>
    /// 从视频抽一帧存为 JPG。成功返回 true。
    /// </summary>
    public bool ExtractCover(string videoPath, string outputJpg, int width, double ratio = 0.33, CancellationToken token = default)
    {
        if (FfmpegPath is null) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(outputJpg)!);

        var duration = ProbeDuration(videoPath);
        var timestamps = new List<double>();
        if (duration is > 1)
        {
            var t = Math.Max(1.0, duration.Value * Math.Clamp(ratio, 0.05, 0.9));
            timestamps.Add(t);
            timestamps.Add(Math.Max(1.0, duration.Value * 0.15));
            timestamps.Add(Math.Max(0.5, duration.Value * 0.5));
            timestamps.Add(3.0);
        }
        else
        {
            timestamps.Add(5.0);
            timestamps.Add(1.0);
        }

        foreach (var ts in timestamps)
        {
            token.ThrowIfCancellationRequested();
            var tmp = outputJpg + ".tmp.jpg";
            TryDelete(tmp);

            // 先尝试带 GPU/硬件加速解码的快速路径（失败无副作用）
            if (TryExtractAt(videoPath, tmp, ts, width))
            {
                try
                {
                    if (new FileInfo(tmp).Length > 1024)
                    {
                        if (File.Exists(outputJpg)) TryDelete(outputJpg);
                        File.Move(tmp, outputJpg);
                        return true;
                    }
                }
                catch
                {
                    // fallthrough
                }
            }

            TryDelete(tmp);
        }

        return false;
    }

    private bool TryExtractAt(string videoPath, string outputPath, double seconds, int width)
    {
        var args = string.Create(CultureInfo.InvariantCulture,
            $"-hide_banner -loglevel error -nostdin -ss {seconds:0.###} -i \"{videoPath}\" -frames:v 1 -vf \"scale={width}:-2\" -q:v 4 -y \"{outputPath}\"");
        var result = Run(FfmpegPath!, args, TimeSpan.FromSeconds(45));
        return result is not null && File.Exists(outputPath);
    }

    private static string? Run(string exe, string args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            _ = stderrTask;
            return stdout;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>从 ffmpeg -i 输出里读时长，兜底用。</summary>
    public static TimeSpan? ParseDurationFromFfmpeg(string stderr)
    {
        var m = Regex.Match(stderr, @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
        if (!m.Success) return null;
        return new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value) * 10);
    }
}
