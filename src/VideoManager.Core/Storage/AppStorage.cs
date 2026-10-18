using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoManager.Core.Models;

namespace VideoManager.Core.Storage;

/// <summary>配置 + 轻量缓存（JSON）。全部放在 %LOCALAPPDATA%\VideoManager。</summary>
public sealed class AppStorage
{
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
        DataDirectory = dataDirectory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoManager");
        CoverDirectory = System.IO.Path.Combine(DataDirectory, "covers");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CoverDirectory);
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
