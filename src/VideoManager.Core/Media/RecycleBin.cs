using System.IO;
using System.Runtime.InteropServices;

namespace VideoManager.Core.Media;

/// <summary>
/// 删除文件时走 Windows 回收站，而不是直接抹掉。
/// 用的是 shell 的 SHFileOperation（FOF_ALLOWUNDO），删完还能在回收站里还原。
/// </summary>
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;

    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmationMkDir = 0x0200;
    private const ushort FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    /// <summary>把一个或多个文件 / 文件夹送进回收站。</summary>
    public static RecycleResult MoveToRecycleBin(IEnumerable<string> paths, IntPtr owner = default)
    {        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p =>
            {
                try { return File.Exists(p) || Directory.Exists(p); }
                catch { return false; }
            })
            .ToList();

        if (list.Count == 0)
        {
            return new RecycleResult(true, 0, 0);
        }

        // pFrom 要的是「路径\0路径\0\0」这种双空结尾的列表。
        // 托管字符串自己还会再补一个 '\0'，多一个结尾空字符不影响解析。
        var from = string.Join('\0', list) + "\0\0";

        var op = new ShFileOpStruct
        {
            hwnd = owner,
            wFunc = FoDelete,
            pFrom = from,
            pTo = null,
            fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofNoConfirmationMkDir | FofSilent | FofNoErrorUi),
            lpszProgressTitle = null,
        };

        try
        {
            var code = SHFileOperation(ref op);
            // 有些错误码是「非致命」的（比如个别文件被占用），已经删掉的那些仍然有效，
            // 所以这里把 code 原样带出去，由调用方按「文件还在不在」判断实际结果。
            return new RecycleResult(code == 0 && !op.fAnyOperationsAborted, code, list.Count);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new RecycleResult(false, -1, list.Count);
        }
    }

    /// <summary>SHFileOperation 的错误码说人话。</summary>
    public static string DescribeError(int code) => code switch
    {
        0 => "",
        0x71 => "回收站拒绝了这个操作",
        0x78 => "访问被拒绝（文件可能正在被播放器占用）",
        0x7C => "路径无效或目标已不存在",
        0x7D => "路径太长",
        0x7E => "不支持这个操作",
        0x80 => "文件正在被别的程序使用",
        0x81 => "磁盘上没有回收站（网络位置或 U 盘常见）",
        0x82 => "磁盘空间不足",
        0xB7 => "已经有一个删除操作在进行了",
        -1 => "系统不支持回收站接口",
        _ => $"系统返回错误码 0x{code:X}",
    };

    /// <summary>回收站不可用（网络盘、U 盘）时的提示，避免用户以为文件还能还原。</summary>
    public static bool ProbablyRecyclable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>删除结果。<see cref="Count"/> 是提交的条目数，不是成功数。</summary>
public readonly record struct RecycleResult(bool Success, int ErrorCode, int Count);
