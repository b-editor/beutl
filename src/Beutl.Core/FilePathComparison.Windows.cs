using System.Runtime.InteropServices;
using System.Text;

namespace Beutl;

public static partial class FilePathComparison
{
    private static string? TryGetWindowsLongPath(string path)
    {
        var buffer = new StringBuilder(32768);
        uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
        return length is > 0 and < 32768
            ? Path.GetFullPath(buffer.ToString())
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(
        string shortPath,
        StringBuilder longPath,
        uint bufferLength);
}
