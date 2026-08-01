namespace Beutl.Editor.VersionControl;

internal sealed class RepositoryWatcher : IDisposable
{
    private static readonly string[] AncestorRuleFileNames = [".gitignore", ".gitattributes"];

    internal static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly string _repoRoot;
    private readonly string _projectRoot;
    private readonly ITimer _debounceTimer;
    private readonly Func<string, FileSystemWatcher> _watcherFactory;
    private readonly Action<FileSystemWatcher> _watcherEnabler;
    private readonly List<FileSystemWatcher> _watchers = [];
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
        string relativePath = Path.GetRelativePath(projectRoot, path).Replace('\\', '/');
        if (relativePath == ".."
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            return true;
        }

        if (relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return relativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is ".git" or ".beutl");
    }

    internal static bool ShouldIncludeGitMetadataPath(string metadataRoot, string path)
    {
        string relativePath = Path.GetRelativePath(metadataRoot, path).Replace('\\', '/');
        if (relativePath == ".."
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relativePath))
        {
            return false;
        }

        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return relativePath is "index" or "HEAD" or "packed-refs" or "refs"
               || relativePath.StartsWith("refs/", StringComparison.Ordinal);
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
        if (ShouldExcludePath(_projectRoot, path))
        {
            return;
        }

        ScheduleChanged();
    }

    internal void NotifyPathRenamed(string oldPath, string newPath)
    {
        if (ShouldExcludePath(_projectRoot, oldPath)
            && ShouldExcludePath(_projectRoot, newPath))
        {
            return;
        }

        ScheduleChanged();
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
            if (!RepositoryPathComparer.AreEquivalent(
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
    }

    private void AddGitMetadataWatcher(
        string watchedDirectory,
        string metadataRoot,
        bool includeSubdirectories)
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
            });
    }

    private void AddWatcher(string directory, Action<FileSystemWatcher> configure)
    {
        FileSystemWatcher watcher = _watcherFactory(directory)
                                    ?? throw new InvalidOperationException(
                                        "The watcher factory returned null.");
        try
        {
            configure(watcher);
            _watchers.Add(watcher);
            try
            {
                _watcherEnabler(watcher);
            }
            catch
            {
                _watchers.Remove(watcher);
                throw;
            }
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    private void RefreshGitRefsWatcher(string metadataRoot)
    {
        string refsDirectory = Path.Combine(metadataRoot, "refs");
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (FileSystemWatcher watcher in _watchers)
            {
                if (watcher.IncludeSubdirectories && PathsEqual(watcher.Path, refsDirectory))
                {
                    return;
                }
            }

            if (Directory.Exists(refsDirectory))
            {
                AddGitMetadataWatcher(refsDirectory, metadataRoot, includeSubdirectories: true);
            }
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
        string refsDirectory = Path.Combine(metadataRoot, "refs");
        if (PathsEqual(e.FullPath, refsDirectory)
            || e is RenamedEventArgs refsRename
            && PathsEqual(refsRename.OldFullPath, refsDirectory))
        {
            RefreshGitRefsWatcher(metadataRoot);
        }

        bool include = ShouldIncludeGitMetadataPath(metadataRoot, e.FullPath);
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
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    private void ScheduleChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
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
