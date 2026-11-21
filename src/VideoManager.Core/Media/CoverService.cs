using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoManager.Core.Media;

/// <summary>调用 ffmpeg / ffprobe 抽取封面帧。</summary>
public sealed class CoverService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public string? FfmpegPath { get; private set; }
    public string? FfprobePath { get; private set; }

    public CoverService(string? configuredFfmpeg = null)
    {
        var ff = Locate(configuredFfmpeg, "ffmpeg.exe");
        var ffDir = ff is null ? null : Path.GetDirectoryName(ff);
        var fp = ffDir is null ? null : Locate(Path.Combine(ffDir, "ffprobe.exe"), "ffprobe.exe");
        FfmpegPath = ff;
        FfprobePath = fp ?? Locate(null, "ffprobe.exe");
    }

    public bool Available => FfmpegPath is not null;

    public static string? Locate(string? configured, string exeName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var p = configured.Trim().Trim('"');
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
                var candidate = Path.Combine(dir.Trim().Trim('"'), exeName);
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
    public double? ProbeDuration(string videoPath, CancellationToken token = default)
    {
        if (FfprobePath is null) return null;
        string[] args =
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            videoPath,
        ];
        var output = Run(FfprobePath, args, TimeSpan.FromSeconds(20), token);
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
    /// <param name="variant">
    /// 第几次重抓。0 = 默认位置；每次 +1 会换一个完全不同的时间点，
    /// 否则「重抓封面」永远抽到同一帧，看起来像没生效。
    /// </param>
    public bool ExtractCover(string videoPath, string outputJpg, int width, double ratio = 0.33,
        CancellationToken token = default, int variant = 0)
    {
        if (FfmpegPath is null) return false;

        var fullOutput = Path.GetFullPath(outputJpg);
        var dir = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var gate = FileLocks.GetOrAdd(fullOutput, static _ => new SemaphoreSlim(1, 1));
        gate.Wait(token);
        try
        {
            var duration = ProbeDuration(videoPath, token);
            var timestamps = new List<double>();
            if (duration is > 1)
            {
                var dur = duration.Value;
                if (variant <= 0)
                {
                    timestamps.Add(Math.Max(1.0, dur * Math.Clamp(ratio, 0.05, 0.9)));
                    timestamps.Add(Math.Max(1.0, dur * 0.15));
                    timestamps.Add(Math.Max(0.5, dur * 0.5));
                    timestamps.Add(3.0);
                }
                else
                {
                    // 用黄金分割比在时间轴上跳点：连续重抓会依次落到
                    // 约 10%、48%、86%、34%、72% … 的位置，几乎不会重复。
                    var offset = (variant * 0.381_966_011_25) % 0.8;
                    for (var k = 0; k < 4; k++)
                    {
                        var fraction = 0.1 + ((offset + k * 0.25) % 0.8);
                        timestamps.Add(Math.Max(0.5, Math.Min(dur - 0.5, dur * fraction)));
                    }
                }
            }
            else
            {
                timestamps.Add(5.0);
                timestamps.Add(1.0);
            }

            foreach (var ts in timestamps.Distinct())
            {
                token.ThrowIfCancellationRequested();
                var tmp = $"{fullOutput}.{Guid.NewGuid():N}.tmp.jpg";
                try
                {
                    if (TryExtractAt(videoPath, tmp, ts, width, token))
                    {
                        var info = new FileInfo(tmp);
                        if (info.Exists && info.Length > 1024)
                        {
                            File.Move(tmp, fullOutput, overwrite: true);
                            return true;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // fallthrough
                }
                finally
                {
                    TryDelete(tmp);
                }
            }

            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryExtractAt(string videoPath, string outputPath, double seconds, int width, CancellationToken token)
    {
        var ss = seconds.ToString("0.###", CultureInfo.InvariantCulture);
        var scale = $"scale={width}:-2";
        string[] args =
        [
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-ss", ss,
            "-i", videoPath,
            "-frames:v", "1",
            "-vf", scale,
            "-q:v", "4",
            "-y",
            outputPath,
        ];
        var result = Run(FfmpegPath!, args, TimeSpan.FromSeconds(45), token);
        return result is not null && File.Exists(outputPath);
    }

    private static string? Run(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token)
    {
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            process = Process.Start(psi);
            if (process is null) return null;

            // 先异步消费 stdout/stderr，再受超时与取消控制等待进程退出，避免管道阻塞导致超时失效。
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                process.WaitForExitAsync(timeoutCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (token.IsCancellationRequested)
                {
                    throw;
                }
                return null;
            }

            // 进程已退出，短暂等待流排空
            Task.WhenAll(stdoutTask, stderrTask).Wait(TimeSpan.FromSeconds(2));

            if (process.ExitCode != 0)
            {
                return null;
            }

            return stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
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
}
