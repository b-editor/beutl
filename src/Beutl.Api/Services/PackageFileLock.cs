namespace Beutl.Api.Services;

internal static class PackageFileLock
{
    public static FileStream Open(string path)
        => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public static bool IsContention(IOException exception)
        // .NET reports ERROR_SHARING_VIOLATION on Windows and EWOULDBLOCK on Unix.
        => exception.HResult == (OperatingSystem.IsWindows() ? unchecked((int)0x80070020)
            : OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? 35 : 11);

    public static FileStream Acquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            try { return Open(path); }
            catch (IOException ex) when (IsContention(ex)) { Thread.Sleep(25); }
        }
    }

    public static async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return Open(path); }
            catch (IOException ex) when (IsContention(ex))
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
