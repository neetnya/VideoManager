namespace VideoManager.Core.Models;

/// <summary>一个媒体库根目录。</summary>
public sealed class LibraryRoot
{
    public required string Path { get; init; }
    public bool Enabled { get; set; } = true;
    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;

    public string Name
    {
        get
        {
            var p = Path.TrimEnd('\\', '/');
            var n = System.IO.Path.GetFileName(p);
            return string.IsNullOrEmpty(n) ? p : n;
        }
    }

    public static LibraryRoot Create(string path) => new() { Path = System.IO.Path.GetFullPath(path) };
}
