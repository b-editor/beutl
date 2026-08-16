namespace Beutl.Services.AI;

internal static class AiTemporaryFileStore
{
    internal static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly object s_gate = new();
    private static readonly HashSet<string> s_cleanedDirectories = new(StringComparer.Ordinal);

    public static string RootDirectory => Path.Combine(
        BeutlEnvironment.GetHomeDirectoryPath(),
        "tmp",
        "ai");

    public static (string Path, FileStream Stream) Create(
        string category,
        string prefix,
        string extension)
    {
        string directory = GetCategoryDirectory(category);
        string normalizedPrefix = NormalizeComponent(prefix, nameof(prefix));
        string normalizedExtension = NormalizeExtension(extension);
        EnsurePrivateDirectory(directory);
        CleanStaleFilesOnce(directory, DateTimeOffset.UtcNow);

        while (true)
        {
            string path = Path.Combine(
                directory,
                $"{normalizedPrefix}-{Guid.NewGuid():N}{normalizedExtension}");
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = System.IO.FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 81920,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = FileMode;
                var stream = new FileStream(path, options);
                try
                {
                    EnsurePrivateFile(path);
                    return (path, stream);
                }
                catch
                {
                    stream.Dispose();
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                    }
                    throw;
                }
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
    }

    internal static string GetCategoryDirectory(string category)
        => Path.Combine(RootDirectory, NormalizeComponent(category, nameof(category)));

    internal static void CleanStaleFiles(string directory, DateTimeOffset now)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(path) >= StaleAge)
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static void EnsurePrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(RootDirectory, DirectoryMode);
            if (!string.Equals(directory, RootDirectory, StringComparison.Ordinal))
                File.SetUnixFileMode(directory, DirectoryMode);
        }
    }

    internal static void EnsurePrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, FileMode);
    }

    private static void CleanStaleFilesOnce(string directory, DateTimeOffset now)
    {
        lock (s_gate)
        {
            if (!s_cleanedDirectories.Add(directory))
                return;
        }
        CleanStaleFiles(directory, now);
    }

    private static string NormalizeComponent(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 64
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("The temporary-file component is invalid.", parameterName);
        }
        return normalized;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.StartsWith('.') ? extension : $".{extension}";
        if (normalized.Length is < 2 or > 11
            || normalized.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The temporary-file extension is invalid.", nameof(extension));
        }
        return normalized.ToLowerInvariant();
    }
}
