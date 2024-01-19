using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Beutl;

internal static class StringHash
{
    public static string GetMD5Hash(this string str)
    {
        byte[] hash = MD5.HashData(MemoryMarshal.Cast<char, byte>(str.AsSpan()));

        return Convert.ToHexString(hash);
    }
}
