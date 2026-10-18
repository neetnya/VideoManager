using System.Security.Cryptography;
using System.Text;

namespace VideoManager.Core.Util;

public static class Hashing
{
    public static string ShortSha1(string text, int length = 20)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString(0, Math.Min(length, sb.Length));
    }
}
