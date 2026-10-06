using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Beutl.Api.Services;

internal static class PrivateDurableFile
{
    public static bool IsFileLocked(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void WritePrivateBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using FileStream stream = new(path, options);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
        RestrictFile(path);
    }

    public static void AtomicReplace(string temporary, string destination, bool overwrite, string failureMessage)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(temporary, destination, overwrite);
            return;
        }
        const uint replace = 0x1;
        const uint writeThrough = 0x8;
        if (!MoveFileEx(temporary, destination, writeThrough | (overwrite ? replace : 0)))
            throw new IOException(failureMessage, new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    /// <summary>
    /// Unix directory fsync closes the rename durability window. Windows has
    /// no portable directory fsync; file bytes are flushed and rename is
    /// atomic, while directory-entry persistence remains filesystem-defined.
    /// </summary>
    public static void EnsureDirectorySynced(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        int fd = UnixOpen(path, 0);
        if (fd < 0)
            throw new IOException($"Unable to open directory for durability sync (errno {Marshal.GetLastWin32Error()}).");
        try
        {
            if (UnixFsync(fd) != 0)
                throw new IOException($"Unable to fsync directory (errno {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            if (UnixClose(fd) != 0)
                throw new IOException($"Unable to close synced directory (errno {Marshal.GetLastWin32Error()}).");
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int UnixFsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int fd);

    public static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
