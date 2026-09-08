using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Beutl.Editor.VersionControl;

internal static partial class AtomicFileExchange
{
    private const int AtCurrentWorkingDirectory = -100;
    private const int ErrorUnableToMoveReplacement2 = 1177;
    private const uint RenameExchange = 0x00000002;
    private const uint ReplaceFileIgnoreMergeErrors = 0x00000002;

    public static string ReplacePreservingTarget(string targetPath, string replacementPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPath);

        string target = Path.GetFullPath(targetPath);
        string replacement = Path.GetFullPath(replacementPath);
        string targetDirectory = Path.GetDirectoryName(target)
                                 ?? throw new ArgumentException(
                                     "The target must have a parent directory.",
                                     nameof(targetPath));
        string replacementDirectory = Path.GetDirectoryName(replacement)
                                      ?? throw new ArgumentException(
                                          "The replacement must have a parent directory.",
                                          nameof(replacementPath));
        if (!string.Equals(targetDirectory, replacementDirectory, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Atomic file exchange requires sibling paths.",
                nameof(replacementPath));
        }

        if (OperatingSystem.IsWindows())
        {
            string displacedPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.displaced.tmp");
            if (ReplaceFileW(
                    target,
                    replacement,
                    displacedPath,
                    ReplaceFileIgnoreMergeErrors,
                    0,
                    0) == 0)
            {
                HandleWindowsExchangeFailure(
                    target,
                    replacement,
                    displacedPath,
                    Marshal.GetLastPInvokeError());
            }

            return displacedPath;
        }

        int result;
        if (OperatingSystem.IsLinux())
        {
            result = RenameAt2(
                AtCurrentWorkingDirectory,
                replacement,
                AtCurrentWorkingDirectory,
                target,
                RenameExchange);
        }
        else if (OperatingSystem.IsMacOS())
        {
            result = RenameX(replacement, target, RenameExchange);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Atomic repository-hygiene updates are not supported on this platform.");
        }

        if (result != 0)
        {
            ThrowExchangeFailure(target, replacement, Marshal.GetLastPInvokeError());
        }

        // renameat2(RENAME_EXCHANGE) and renamex_np(RENAME_SWAP) leave the displaced target at
        // the replacement path. The caller verifies that exact snapshot before deleting it.
        return replacement;
    }

    internal static void HandleWindowsExchangeFailure(
        string target,
        string replacement,
        string displacedPath,
        int error)
    {
        if (error == ErrorUnableToMoveReplacement2)
        {
            try
            {
                File.Move(displacedPath, target, overwrite: false);
            }
            catch (Exception recoveryFailure)
                when (recoveryFailure is IOException or UnauthorizedAccessException)
            {
                throw new AtomicFileExchangeException(
                    $"Could not restore '{target}' after a partial atomic exchange. The displaced target was retained at '{displacedPath}'.",
                    error,
                    displacedPath,
                    recoveryFailure);
            }
        }

        ThrowExchangeFailure(target, replacement, error);
    }

    private static void ThrowExchangeFailure(string target, string replacement, int error)
    {
        throw new IOException(
            $"Could not atomically exchange '{target}' with '{replacement}': "
            + new Win32Exception(error).Message);
    }

    [LibraryImport(
        "libc",
        EntryPoint = "renameat2",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt2(
        int oldDirectory,
        string oldPath,
        int newDirectory,
        string newPath,
        uint flags);

    [LibraryImport(
        "libc",
        EntryPoint = "renamex_np",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameX(string oldPath, string newPath, uint flags);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "ReplaceFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ReplaceFileW(
        string replacedFileName,
        string replacementFileName,
        string backupFileName,
        uint replaceFlags,
        nint exclude,
        nint reserved);
}

internal sealed class AtomicFileExchangeException(
    string message,
    int nativeError,
    string displacedPath,
    Exception innerException) : IOException(message, innerException)
{
    internal int NativeError { get; } = nativeError;

    internal string DisplacedPath { get; } = displacedPath;
}
