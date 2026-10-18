using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoManager.Core.Models;

namespace VideoManager.Core.Storage;

/// <summary>配置 + 轻量缓存（JSON）。默认放在程序（exe）所在目录，跟着程序走。</summary>
public sealed class AppStorage
{
    /// <summary>老版本的存放位置（%LOCALAPPDATA%\VideoManager），仅用于一次性搬家。</summary>
    private static string LegacyDataDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoManager");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // WindowLeft / WindowTop 的默认值是 double.NaN（还没存过窗口位置）。
        // System.Text.Json 默认拒绝把 NaN 写成 JSON 数字，会直接抛异常 ——
        // 而 SaveSettings 的 catch 又会把异常吞掉，结果就是「配置永远存不下来」。
        // 允许 "NaN" 这样的具名浮点字面量，读写都不会再炸。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string DataDirectory { get; }
    public string CoverDirectory { get; }
    public string SettingsFile => System.IO.Path.Combine(DataDirectory, "settings.json");
    public string CacheFile => System.IO.Path.Combine(DataDirectory, "library-cache.json");

    public AppStorage(string? dataDirectory = null)
    {
        var preferred = dataDirectory is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        DataDirectory = preferred;
        CoverDirectory = System.IO.Path.Combine(DataDirectory, "covers");

        if (TryPrepare(DataDirectory, CoverDirectory))
        {
            if (dataDirectory is null)
            {
                MigrateLegacyData();
            }
            return;
        }

        // 程序目录不可写（例如装在 Program Files 下、或 U 盘只读），退回老位置，至少还能用
        DataDirectory = LegacyDataDirectory;
        CoverDirectory = System.IO.Path.Combine(DataDirectory, "covers");
        TryPrepare(DataDirectory, CoverDirectory);
    }

    private static bool TryPrepare(string dataDirectory, string coverDirectory)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(coverDirectory);

            // 目录存在不代表能写（Program Files），实写一次最保险
            var probe = System.IO.Path.Combine(dataDirectory, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 老版本把数据放在 %LOCALAPPDATA%\VideoManager，新版改到程序目录。
    /// 首次以新位置启动时把老数据整份搬过来 —— 只在「新位置还没有配置」时搬，避免覆盖用户后来的改动。
    /// </summary>
    private void MigrateLegacyData()
    {
        try
        {
            var legacy = LegacyDataDirectory;
            if (!Directory.Exists(legacy))
            {
                return;
            }

            if (string.Equals(
                    System.IO.Path.GetFullPath(legacy).TrimEnd('\\'),
                    System.IO.Path.GetFullPath(DataDirectory).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var legacySettings = System.IO.Path.Combine(legacy, "settings.json");
            if (!File.Exists(legacySettings) || File.Exists(SettingsFile))
            {
                return;
            }

            File.Copy(legacySettings, SettingsFile, overwrite: false);

            var legacyCache = System.IO.Path.Combine(legacy, "library-cache.json");
            if (File.Exists(legacyCache))
            {
                File.Copy(legacyCache, CacheFile, overwrite: false);
            }

            var legacyCovers = System.IO.Path.Combine(legacy, "covers");
            if (Directory.Exists(legacyCovers))
            {
                foreach (var source in Directory.GetFiles(legacyCovers, "*.jpg"))
                {
                    try
                    {
                        var target = System.IO.Path.Combine(CoverDirectory, System.IO.Path.GetFileName(source));
                        File.Copy(source, target, overwrite: false);
                    }
                    catch
                    {
                        // 单张封面失败无所谓，回头会重新抓
                    }
                }
            }
        }
        catch
        {
            // 搬家失败不影响启动，最坏情况是重新扫一遍
        }
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (s is not null)
                {
                    return s;
                }
            }
        }
        catch
        {
            // 配置损坏则回退默认值
        }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tmp = SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsFile, overwrite: true);
        }
        catch
        {
            // 保存失败不致命
        }
    }

    public List<CachedSeries> LoadCache()
    {
        try
        {
            if (File.Exists(CacheFile))
            {
                var json = File.ReadAllText(CacheFile);
                return JsonSerializer.Deserialize<List<CachedSeries>>(json, JsonOptions) ?? [];
            }
        }
        catch
        {
            // 缓存损坏则重新扫描
        }
        return [];
    }

    public void SaveCache(IEnumerable<CachedSeries> series)
    {
        try
        {
            var json = JsonSerializer.Serialize(series.ToList(), JsonOptions);
            var tmp = CacheFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, CacheFile, overwrite: true);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>丢弃缓存文件（解析规则升级后旧标题不可再用）。</summary>
    public void ClearCache()
    {
        try
        {
            if (File.Exists(CacheFile))
            {
                File.Delete(CacheFile);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>封面文件名：以剧集 Key 的哈希命名，避免非法字符。</summary>
    public string CoverPathFor(string seriesKey)
    {
        var hash = Util.Hashing.ShortSha1(seriesKey, 20);
        return System.IO.Path.Combine(CoverDirectory, hash + ".jpg");
    }

    public void ClearCovers()
    {
        try
        {
            if (!Directory.Exists(CoverDirectory)) return;
            foreach (var f in Directory.GetFiles(CoverDirectory, "*.jpg"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch
        {
            // ignore
        }
    }
}

public sealed class CachedSeries
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string? CoverPath { get; set; }
    public List<CachedFile> Files { get; set; } = [];
}

public sealed class CachedFile
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string? ShowTitle { get; set; }
    public string? EpisodeTitle { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public int? SpecialNumber { get; set; }
    public bool IsSpecial { get; set; }
    public string? ReleaseGroup { get; set; }
    public long SizeBytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
}
