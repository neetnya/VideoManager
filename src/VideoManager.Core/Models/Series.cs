namespace VideoManager.Core.Models;

/// <summary>一部「剧集」（一个系列）。</summary>
public sealed class Series
{
    public required string Key { get; init; }

    /// <summary>剧名（可被用户覆盖）。</summary>
    public string Title { get; set; } = "";

    /// <summary>自动推导出的原始剧名，用于重置。</summary>
    public string OriginalTitle { get; init; } = "";

    public required string RootPath { get; init; }
    public required string FolderPath { get; init; }

    public List<MediaFile> Files { get; } = [];

    /// <summary>自动生成的封面（取自第一个视频）。</summary>
    public string? CoverPath { get; set; }

    /// <summary>用户指定的封面。</summary>
    public string? CustomCover { get; set; }

    /// <summary>季标签，例如 "S1" 或 "S1–S3"。</summary>
    public string SeasonLabel { get; set; } = "";

    public MediaFile? FirstFile => Files.Count > 0 ? Files[0] : null;

    public string EffectiveCover => CustomCover is { Length: > 0 } c && File.Exists(c)
        ? c
        : CoverPath ?? "";
}
