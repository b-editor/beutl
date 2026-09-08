namespace Beutl.Editor.VersionControl;

internal sealed class RepositoryWatcher : IDisposable
{
    private static readonly string[] AncestorRuleFileNames = [".gitignore", ".gitattributes"];

    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan MaximumDebounceDelay = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly string _repoRoot;
    private readonly string _projectRoot;
    private readonly ITimer _debounceTimer;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, FileSystemWatcher> _watcherFactory;
    private readonly Action<FileSystemWatcher> _watcherEnabler;
    private readonly List<FileSystemWatcher> _watchers = [];
    private string[] _requiredTemporaryPaths = [];
    private long? _debounceWindowStartedTimestamp;
    private bool _disposed;

    internal RepositoryWatcher(RepositoryInfo repository, TimeProvider? timeProvider = null)
        : this(repository, timeProvider ?? TimeProvider.System, startWatching: true)
    {
    }

    internal RepositoryWatcher(
        RepositoryInfo repository,
        TimeProvider timeProvider,
        bool startWatching,
        Func<string, FileSystemWatcher>? watcherFactory = null,
        Action<FileSystemWatcher>? watcherEnabler = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _repoRoot = repository.RepoRoot;
        _projectRoot = repository.ProjectRoot;
        _timeProvider = timeProvider;
        _watcherFactory = watcherFactory ?? (static path => new FileSystemWatcher(path));
        _watcherEnabler = watcherEnabler ?? (static watcher => watcher.EnableRaisingEvents = true);
        _debounceTimer = timeProvider.CreateTimer(
            static state => ((RepositoryWatcher)state!).QueueChanged(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        if (startWatching)
        {
            try
            {
                Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    public event EventHandler? Changed;

    internal static bool ShouldExcludePath(string projectRoot, string path)
    {
        return ShouldExcludePath(projectRoot, path, []);
    }

    private static bool ShouldExcludePath(
        string projectRoot,
        string path,
        IReadOnlyList<string> requiredTemporaryPaths)
    {
        if (!TryGetCanonicalRelativePath(projectRoot, path, out string? relativePath))
        {
            return true;
        }

        if (relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            && !requiredTemporaryPaths.Any(requiredPath =>
                VersionControlPathComparison.AreSameCanonicalPath(requiredPath, path)))
        {
            return true;
        }

        string parent = VersionControlPathComparison.ResolveCanonicalPath(projectRoot);
        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (AreSameChildPath(parent, segment, ".git")
                || AreSameChildPath(parent, segment, ".beutl"))
            {
                return true;
            }

            parent = Path.Combine(parent, segment);
        }

        return false;
    }

    internal static bool ShouldIncludeGitMetadataPath(string metadataRoot, string path)
    {
        if (!TryGetCanonicalRelativePath(metadataRoot, path, out string? relativePath))
        {
            return false;
        }

        relativePath = NormalizeDirectorySeparators(relativePath);

        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return relativePath is "index"
                   or "HEAD"
                   or "packed-refs"
                   or "config"
                   or "config.worktree"
                   or "info/exclude"
                   // Repository-local attributes outrank every .gitattributes file, so a change to
                   // text, eol or filter here can make project paths modified on its own.
                   or "info/attributes"
                   or "reftable"
                   or "refs"
               || relativePath.StartsWith("reftable/", StringComparison.Ordinal)
               || relativePath.StartsWith("refs/", StringComparison.Ordinal);
    }

    private static string NormalizeDirectorySeparators(string path)
        => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static bool AreSameChildPath(string parent, string leftName, string rightName)
    {
        return VersionControlPathComparison.TryAreSameChildPath(
                   parent,
                   leftName,
                   rightName,
                   out bool areSame)
               && areSame;
    }

    private static bool TryGetCanonicalRelativePath(
        string root,
        string path,
        out string relativePath)
    {
        try
        {
            string canonicalRoot = Path.TrimEndingDirectorySeparator(
                VersionControlPathComparison.ResolveCanonicalPath(root));
            string canonicalPath = Path.TrimEndingDirectorySeparator(
                VersionControlPathComparison.ResolveCanonicalPath(path));
            if (!VersionControlPathComparison.IsSameOrDescendant(canonicalRoot, canonicalPath))
            {
                relativePath = string.Empty;
                return false;
            }

            if (string.Equals(canonicalRoot, canonicalPath, StringComparison.Ordinal))
            {
                relativePath = ".";
                return true;
            }

            string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
                || canonicalRoot.EndsWith(Path.AltDirectorySeparatorChar)
                    ? canonicalRoot
                    : canonicalRoot + Path.DirectorySeparatorChar;
            if (!canonicalPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                relativePath = string.Empty;
                return false;
            }

            relativePath = canonicalPath[prefix.Length..];
            return true;
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            relativePath = string.Empty;
            return false;
        }
    }

    internal static (string GitDirectory, string CommonDirectory)? ResolveGitMetadataDirectories(
        string repoRoot)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        string dotGitPath = Path.Combine(normalizedRoot, ".git");
        string? gitDirectory;

        if (Directory.Exists(dotGitPath))
        {
            gitDirectory = dotGitPath;
        }
        else if (File.Exists(dotGitPath))
        {
            string pointer;
            try
            {
                pointer = File.ReadLines(dotGitPath).FirstOrDefault()?.Trim() ?? string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            const string Prefix = "gitdir:";
            if (!pointer.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string gitDirectoryValue = pointer[Prefix.Length..].Trim();
            if (string.IsNullOrEmpty(gitDirectoryValue))
            {
                return null;
            }

            gitDirectory = Path.IsPathFullyQualified(gitDirectoryValue)
                ? gitDirectoryValue
                : Path.Combine(normalizedRoot, gitDirectoryValue);
        }
        else
        {
            return null;
        }

        gitDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitDirectory));
        if (!Directory.Exists(gitDirectory))
        {
            return null;
        }

        string commonDirectory = gitDirectory;
        string commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
        if (File.Exists(commonDirectoryFile))
        {
            try
            {
                string commonDirectoryValue = File.ReadLines(commonDirectoryFile)
                    .FirstOrDefault()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(commonDirectoryValue))
                {
                    commonDirectory = Path.IsPathFullyQualified(commonDirectoryValue)
                        ? commonDirectoryValue
                        : Path.Combine(gitDirectory, commonDirectoryValue);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        commonDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory));
        return Directory.Exists(commonDirectory)
            ? (gitDirectory, commonDirectory)
            : null;
    }

    internal void NotifyPathChanged(string path)
    {
        if (ShouldExcludeWatchedPath(path))
        {
            return;
        }

        ScheduleChanged();
    }

    internal void NotifyPathRenamed(string oldPath, string newPath)
    {
        if (ShouldExcludeWatchedPath(oldPath)
            && ShouldExcludeWatchedPath(newPath))
        {
            return;
        }

        ScheduleChanged();
    }

    internal void UpdateRequiredPaths(IReadOnlySet<string> requiredProjectRelativePaths)
    {
        ArgumentNullException.ThrowIfNull(requiredProjectRelativePaths);
        var requiredTemporaryPaths = new List<string>();
        foreach (string relativePath in requiredProjectRelativePaths)
        {
            if (!relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || Path.IsPathFullyQualified(relativePath))
            {
                continue;
            }

            string path = Path.GetFullPath(Path.Combine(
                _projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (VersionControlPathComparison.IsSameOrDescendant(_projectRoot, path))
            {
                requiredTemporaryPaths.Add(path);
            }
        }

        Volatile.Write(ref _requiredTemporaryPaths, [.. requiredTemporaryPaths]);
    }

    internal bool ShouldExcludeWatchedPath(string path)
    {
        return ShouldExcludePath(
            _projectRoot,
            path,
            Volatile.Read(ref _requiredTemporaryPaths));
    }

    public void Dispose()
    {
        FileSystemWatcher[] watchers;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watchers = [.. _watchers];
            _watchers.Clear();
            _debounceTimer.Dispose();
        }

        foreach (FileSystemWatcher watcher in watchers)
        {
            watcher.Dispose();
        }
    }

    private void Start()
    {
        if (!Directory.Exists(_repoRoot))
        {
            throw new DirectoryNotFoundException($"Repository directory not found: {_repoRoot}");
        }

        if (!Directory.Exists(_projectRoot))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {_projectRoot}");
        }

        AddWatcher(
            _projectRoot,
            watcher =>
            {
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter = NotifyFilters.FileName
                                       | NotifyFilters.DirectoryName
                                       | NotifyFilters.LastWrite
                                       | NotifyFilters.Size;
                watcher.Changed += OnFileSystemChanged;
                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemChanged;
                watcher.Error += OnWatcherError;
            });

        AddAncestorRuleWatchers();

        (string GitDirectory, string CommonDirectory)? metadataDirectories
            = ResolveGitMetadataDirectories(_repoRoot);
        if (metadataDirectories is { } directories)
        {
            AddGitMetadataWatchers(directories.GitDirectory);
            if (!VersionControlPathComparison.AreSameCanonicalPath(
                    directories.GitDirectory,
                    directories.CommonDirectory))
            {
                AddGitMetadataWatchers(directories.CommonDirectory);
            }
        }
    }

    private void AddAncestorRuleWatchers()
    {
        string relativeProject = Path.GetRelativePath(_repoRoot, _projectRoot);
        if (relativeProject == ".")
        {
            return;
        }

        string directory = _repoRoot;
        foreach (string segment in relativeProject.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            AddAncestorRuleWatcher(directory);
            directory = Path.Combine(directory, segment);
        }
    }

    private void AddAncestorRuleWatcher(string directory)
    {
        AddWatcher(
            directory,
            watcher =>
            {
                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName
                                       | NotifyFilters.LastWrite
                                       | NotifyFilters.Size;
                foreach (string fileName in AncestorRuleFileNames)
                {
                    watcher.Filters.Add(fileName);
                }

                watcher.Changed += OnAncestorRuleChanged;
                watcher.Created += OnAncestorRuleChanged;
                watcher.Deleted += OnAncestorRuleChanged;
                watcher.Renamed += OnAncestorRuleChanged;
                watcher.Error += OnWatcherError;
            });
    }

    private void AddGitMetadataWatchers(string metadataRoot)
    {
        AddGitMetadataWatcher(metadataRoot, metadataRoot, includeSubdirectories: false);
        RefreshGitRefsWatcher(metadataRoot);
        RefreshGitInfoWatcher(metadataRoot);
        RefreshGitReftableWatcher(metadataRoot);
    }

    private void AddGitMetadataWatcher(
        string watchedDirectory,
        string metadataRoot,
        bool includeSubdirectories,
        bool rejectDuplicate = false)
    {
        AddWatcher(
            watchedDirectory,
            watcher =>
            {
                watcher.IncludeSubdirectories = includeSubdirectories;
                watcher.NotifyFilter = NotifyFilters.FileName
                                       | NotifyFilters.DirectoryName
                                       | NotifyFilters.LastWrite
                                       | NotifyFilters.Size;
                FileSystemEventHandler changed = (_, e) => OnGitMetadataChanged(metadataRoot, e);
                RenamedEventHandler renamed = (_, e) => OnGitMetadataChanged(metadataRoot, e);
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += renamed;
                watcher.Error += OnWatcherError;
            },
            rejectDuplicate
                ? watchers => watchers.Any(watcher =>
                    watcher.IncludeSubdirectories == includeSubdirectories
                    && PathsEqual(watcher.Path, watchedDirectory))
                : null);
    }

    private void AddWatcher(
        string directory,
        Action<FileSystemWatcher> configure,
        Func<IReadOnlyList<FileSystemWatcher>, bool>? conflicts = null)
    {
        FileSystemWatcher watcher = _watcherFactory(directory)
                                    ?? throw new InvalidOperationException(
                                        "The watcher factory returned null.");
        try
        {
            configure(watcher);
            _watcherEnabler(watcher);
            bool accepted;
            lock (_sync)
            {
                accepted = !_disposed && conflicts?.Invoke(_watchers) != true;
                if (accepted)
                {
                    _watchers.Add(watcher);
                }
            }

            if (!accepted)
            {
                watcher.Dispose();
            }
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    private void RefreshGitRefsWatcher(string metadataRoot, bool replaceExisting = false)
    {
        RefreshGitMetadataSubdirectoryWatcher(
            metadataRoot,
            "refs",
            includeSubdirectories: true,
            replaceExisting: replaceExisting);
    }

    private void RefreshGitInfoWatcher(string metadataRoot, bool replaceExisting = false)
    {
        RefreshGitMetadataSubdirectoryWatcher(
            metadataRoot,
            "info",
            includeSubdirectories: false,
            replaceExisting: replaceExisting);
    }

    private void RefreshGitReftableWatcher(string metadataRoot, bool replaceExisting = false)
    {
        RefreshGitMetadataSubdirectoryWatcher(
            metadataRoot,
            "reftable",
            includeSubdirectories: true,
            replaceExisting: replaceExisting);
    }

    private void RefreshGitMetadataSubdirectoryWatcher(
        string metadataRoot,
        string directoryName,
        bool includeSubdirectories,
        bool replaceExisting)
    {
        string directory = Path.Combine(metadataRoot, directoryName);
        List<FileSystemWatcher> replacedWatchers = [];
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            for (int i = _watchers.Count - 1; i >= 0; i--)
            {
                FileSystemWatcher watcher = _watchers[i];
                if (watcher.IncludeSubdirectories == includeSubdirectories
                    && PathsEqual(watcher.Path, directory))
                {
                    if (!replaceExisting)
                    {
                        return;
                    }

                    _watchers.RemoveAt(i);
                    replacedWatchers.Add(watcher);
                }
            }
        }

        foreach (FileSystemWatcher watcher in replacedWatchers)
        {
            watcher.Dispose();
        }

        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            bool shouldAttach;
            lock (_sync)
            {
                shouldAttach = !_disposed
                    && !_watchers.Any(watcher =>
                        watcher.IncludeSubdirectories == includeSubdirectories
                        && PathsEqual(watcher.Path, directory))
                    && Directory.Exists(directory);
            }

            if (shouldAttach)
            {
                AddGitMetadataWatcher(
                    directory,
                    metadataRoot,
                    includeSubdirectories,
                    rejectDuplicate: true);
            }
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or ArgumentException)
        {
            // The metadata directory can disappear again between the root event and watcher setup.
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (e is RenamedEventArgs renamed)
        {
            NotifyPathRenamed(renamed.OldFullPath, renamed.FullPath);
        }
        else
        {
            NotifyPathChanged(e.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        ScheduleChanged();
    }

    private void OnAncestorRuleChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleChanged();
    }

    private void OnGitMetadataChanged(string metadataRoot, FileSystemEventArgs e)
    {
        try
        {
            OnGitMetadataChangedCore(metadataRoot, e);
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException
                  or NotSupportedException
                  or ArgumentException)
        {
            // Metadata paths can be replaced between event delivery and canonical comparison.
            // Uncertainty still means Git state may have changed, so refresh conservatively.
            ScheduleChanged();
        }
    }

    private void OnGitMetadataChangedCore(string metadataRoot, FileSystemEventArgs e)
    {
        bool metadataSubdirectoryChanged = false;
        string refsDirectory = Path.Combine(metadataRoot, "refs");
        if (PathsEqual(e.FullPath, refsDirectory)
            || e is RenamedEventArgs refsRename
            && PathsEqual(refsRename.OldFullPath, refsDirectory))
        {
            RefreshGitRefsWatcher(metadataRoot, replaceExisting: true);
            metadataSubdirectoryChanged = true;
        }

        string infoDirectory = Path.Combine(metadataRoot, "info");
        if (PathsEqual(e.FullPath, infoDirectory)
            || e is RenamedEventArgs infoRename
            && PathsEqual(infoRename.OldFullPath, infoDirectory))
        {
            RefreshGitInfoWatcher(metadataRoot, replaceExisting: true);
            metadataSubdirectoryChanged = true;
        }

        string reftableDirectory = Path.Combine(metadataRoot, "reftable");
        if (PathsEqual(e.FullPath, reftableDirectory)
            || e is RenamedEventArgs reftableRename
            && PathsEqual(reftableRename.OldFullPath, reftableDirectory))
        {
            RefreshGitReftableWatcher(metadataRoot, replaceExisting: true);
            metadataSubdirectoryChanged = true;
        }

        bool include = metadataSubdirectoryChanged
                       || ShouldIncludeGitMetadataPath(metadataRoot, e.FullPath);
        if (e is RenamedEventArgs renamed)
        {
            include |= ShouldIncludeGitMetadataPath(metadataRoot, renamed.OldFullPath);
        }

        if (include)
        {
            ScheduleChanged();
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return VersionControlPathComparison.AreSameCanonicalPath(left, right);
    }

    private void ScheduleChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            long now = _timeProvider.GetTimestamp();
            _debounceWindowStartedTimestamp ??= now;
            TimeSpan elapsed = _timeProvider.GetElapsedTime(
                _debounceWindowStartedTimestamp.Value,
                now);
            TimeSpan maximumRemaining = MaximumDebounceDelay - elapsed;
            TimeSpan dueTime = maximumRemaining <= TimeSpan.Zero
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(Math.Min(
                    DebounceInterval.Ticks,
                    maximumRemaining.Ticks));
            _debounceTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceWindowStartedTimestamp = null;
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state => ((RepositoryWatcher)state!).RaiseChanged(),
            this,
            preferLocal: false);
    }

    private void RaiseChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
