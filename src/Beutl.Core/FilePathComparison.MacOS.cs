using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Beutl;

public static partial class FilePathComparison
{
    private static readonly UTF8Encoding s_strictFileNameUtf8 = new(false, true);

    private static unsafe string? TryGetMacExistingEntry(string parent, string component, string candidate)
    {
        // Preserve the strict resolver's directory-read requirement. getattrlist alone also
        // succeeds through traverse-only parents; those must retain the existing fallback rules.
        nint directory = OpenMacDirectory(parent);
        if (directory == 0)
        {
            return null;
        }

        try
        {
            var attributes = new MacAttributeList { BitmapCount = 5, CommonAttributes = 1 }; // ATTR_CMN_NAME
            const int BufferSize = 4096;
            byte* buffer = stackalloc byte[BufferSize];
            // FSOPT_NOFOLLOW preserves the final symlink's own name. The main resolver still
            // follows it before interpreting subsequent dot segments.
            if (GetMacAttributes(candidate, ref attributes, buffer, BufferSize, 1) != 0)
            {
                return null;
            }

            string? name = DecodeMacEntryName(new ReadOnlySpan<byte>(buffer, BufferSize));
            if (name is null
                || !string.Equals(name.Normalize(NormalizationForm.FormC),
                    component.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.Combine(parent, name);
        }
        finally
        {
            CloseMacDirectory(directory);
        }
    }

    // Darwin's attribute buffer begins with its total uint32 length, followed by an
    // attrreference_t: a signed offset relative to the reference and a uint32 byte length.
    internal static string? DecodeMacEntryName(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 12)
        {
            return null;
        }

        uint totalLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        long start = 4L + BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]);
        if (totalLength > buffer.Length || start < 12 || length < 2 || start + length > totalLength)
        {
            return null;
        }

        ReadOnlySpan<byte> name = buffer.Slice((int)start, (int)length);
        if (name[^1] != 0 || name[..^1].IndexOfAny((byte)0, (byte)'/') >= 0)
        {
            return null;
        }

        try
        {
            string decoded = s_strictFileNameUtf8.GetString(name[..^1]);
            return decoded is "." or ".." ? null : decoded;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacAttributeList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint CommonAttributes;
        public uint VolumeAttributes;
        public uint DirectoryAttributes;
        public uint FileAttributes;
        public uint ForkAttributes;
    }

    [DllImport("libSystem.B.dylib", EntryPoint = "getattrlist", SetLastError = true)]
    private static extern unsafe int GetMacAttributes(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        ref MacAttributeList attributes, byte* buffer, nuint bufferSize, nuint options);

    [DllImport("libSystem.B.dylib", EntryPoint = "opendir", SetLastError = true)]
    private static extern nint OpenMacDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libSystem.B.dylib", EntryPoint = "closedir", SetLastError = true)]
    private static extern int CloseMacDirectory(nint directory);
}
