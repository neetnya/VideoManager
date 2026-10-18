using System.Text;
using VideoManager.Core.Library;
using VideoManager.Core.Models;
using VideoManager.Core.Parsing;

Console.OutputEncoding = Encoding.UTF8;

var paths = new[]
{
    @"E:\古畑任三郎",
    @"E:\download\新建文件夹\美妙天堂",
    @"D:\Anime",
};

if (args.Length > 0 && args[0] == "parse")
{
    foreach (var name in args.Skip(1))
    {
        var p = NameParser.Parse(name);
        Console.WriteLine($"{name}\n  show={p.ShowTitle} | ep={p.Episode} | s={p.Season} | epTitle={p.EpisodeTitle} | sp={p.IsSpecial}/{p.SpecialNumber} | grp={p.ReleaseGroup}");
    }
    return;
}

if (args.Length > 1 && args[0] == "cover")
{
    var svc = new VideoManager.Core.Media.CoverService();
    Console.WriteLine($"ffmpeg={svc.FfmpegPath} ffprobe={svc.FfprobePath}");
    var video = args[1];
    var outPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "vm-cover-test.jpg");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var d = svc.ProbeDuration(video);
    Console.WriteLine($"duration={d}");
    var ok = svc.ExtractCover(video, outPath, 480);
    Console.WriteLine($"cover ok={ok} -> {outPath} ({sw.ElapsedMilliseconds} ms, {new FileInfo(outPath).Length} bytes)");
    return;
}

foreach (var path in paths)
{
    if (!Directory.Exists(path))
    {
        Console.WriteLine($"缺失：{path}");
        continue;
    }

    var root = LibraryRoot.Create(path);
    var result = LibraryScanner.Scan([root]);
    Console.WriteLine($"\n===== {path} =====  文件 {result.FileCount}，作品 {result.Series.Count}，跳过 {result.Skipped}，耗时 {result.Elapsed.TotalSeconds:0.0}s");

    foreach (var s in result.Series.OrderByDescending(s => s.Files.Count))
    {
        var first = s.FirstFile;
        Console.WriteLine($"  [{s.Files.Count,3}] {s.Title}   ({s.SeasonLabel})  文件夹={s.FolderPath}");
        if (first is not null)
        {
            Console.WriteLine($"        首集：{first.FileName}  → {first.EpisodeLabel} {first.DisplayName}");
        }
    }
}
