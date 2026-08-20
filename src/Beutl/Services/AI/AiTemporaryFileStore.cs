namespace Beutl.Services.AI;

internal static class AiTemporaryFileStore
{
    internal static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const string SessionLockName = ".lock";
    private static readonly object s_gate = new();
    private static readonly HashSet<string> s_cleanedDirectories = new(StringComparer.Ordinal);
    private static readonly string s_sessionId = Guid.NewGuid().ToString("N");
    private static readonly Dictionary<string, FileStream> s_sessionLocks =
        new(StringComparer.Ordinal);

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
        HoldSession(directory);
        CleanAbandonedSessionsOnce(
            GetCategoryRootDirectory(category),
            DateTimeOffset.UtcNow);

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

    /// <summary>
    /// Where this process writes. Every process keeps its own directory, so
    /// clearing up after one that exited can never take a file another is
    /// still using.
    /// </summary>
    internal static string GetCategoryDirectory(string category)
        => Path.Combine(GetCategoryRootDirectory(category), s_sessionId);

    internal static string GetCategoryRootDirectory(string category)
        => Path.Combine(RootDirectory, NormalizeComponent(category, nameof(category)));

    /// <summary>
    /// Clears up after sessions that are over. A session directory whose lock
    /// is still held belongs to a process that is still running, and nothing in
    /// it is removed however old it looks.
    /// </summary>
    internal static void CleanAbandonedSessions(string categoryRoot, DateTimeOffset now)
    {
        if (!Directory.Exists(categoryRoot))
            return;

        // Files written directly here belong to a version that had no sessions.
        CleanStaleFiles(categoryRoot, now);

        foreach (string session in Directory.EnumerateDirectories(categoryRoot))
        {
            if (string.Equals(Path.GetFileName(session), s_sessionId, StringComparison.Ordinal))
                continue;

            FileStream? claimed;
            try
            {
                claimed = OpenSessionLock(session);
            }
            catch (IOException)
            {
                // Held by a live process.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            using (claimed)
            {
                CleanStaleFiles(session, now);
            }

            TryRemoveFinishedSession(session);
        }
    }

    internal static void CleanStaleFiles(string directory, DateTimeOffset now)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(path), SessionLockName, StringComparison.Ordinal))
                continue;

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
        if (OperatingSystem.IsWindows())
            return;

        // Every level down from the root, so a category or session directory
        // created along the way is no more readable than the root itself.
        File.SetUnixFileMode(RootDirectory, DirectoryMode);
        for (string? current = directory;
             current is not null
             && current.StartsWith(RootDirectory, StringComparison.Ordinal)
             && !string.Equals(current, RootDirectory, StringComparison.Ordinal);
             current = Path.GetDirectoryName(current))
        {
            File.SetUnixFileMode(current, DirectoryMode);
        }
    }

    internal static void EnsurePrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, FileMode);
    }

    private static void CleanAbandonedSessionsOnce(string categoryRoot, DateTimeOffset now)
    {
        lock (s_gate)
        {
            if (!s_cleanedDirectories.Add(categoryRoot))
                return;
        }
        CleanAbandonedSessions(categoryRoot, now);
    }

    // Held for as long as this process runs. Another process reads the lock it
    // cannot take as "the owner is still here".
    private static void HoldSession(string sessionDirectory)
    {
        lock (s_gate)
        {
            if (s_sessionLocks.ContainsKey(sessionDirectory))
                return;

            try
            {
                s_sessionLocks[sessionDirectory] = OpenSessionLock(sessionDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Without the lock the directory only looks abandoned to another
                // process once its files are stale, which is what the store did
                // before sessions existed.
            }
        }
    }

    private static FileStream OpenSessionLock(string sessionDirectory)
        => new(
            Path.Combine(sessionDirectory, SessionLockName),
            System.IO.FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

    private static void TryRemoveFinishedSession(string sessionDirectory)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(sessionDirectory)
                .Any(entry => !string.Equals(
                    Path.GetFileName(entry),
                    SessionLockName,
                    StringComparison.Ordinal)))
            {
                return;
            }

            File.Delete(Path.Combine(sessionDirectory, SessionLockName));
            Directory.Delete(sessionDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
