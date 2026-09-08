using System.Collections.Concurrent;
using System.Text;
using Avalonia.Threading;
using Beutl.Editor.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.FileBrowserTab.Services;

// ディレクトリ変更を監視し、デバウンス付きで変更通知を発行する。
internal sealed class DirectoryWatcherService : IDisposable
{
    private sealed record DirectoryIdentity(string LinkFingerprint, string CanonicalPath);

    private readonly record struct SpecialDirectoryMembershipKey(
        string TemplatesDirectory,
        string MaterialsDirectory,
        string CandidateDirectory);

    private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(300);
    private readonly ILogger _logger = Log.CreateLogger<DirectoryWatcherService>();
    private readonly object _stateSync = new();
    private readonly TimeSpan _debounceInterval;
    private readonly Action<Action> _postDelivery;
    private readonly Action<FileSystemWatcher> _startWatcher;
    private readonly ConcurrentDictionary<SpecialDirectoryMembershipKey, bool>
        _templateOrMaterialDirectories = new();
    private readonly ConcurrentDictionary<string, string> _configuredSpecialDirectoryIdentities =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DirectoryIdentity> _directoryIdentities =
        new(StringComparer.Ordinal);
    // Limit consecutive rearm attempts because persistent failures recur immediately.
    private const int MaxErrorRearms = 3;

    private FileSystemWatcher? _watcher;
    private string? _watchedCanonicalPath;
    private string? _watchedRequestedPath;
    private CancellationTokenSource? _debounceCts;
    private int _errorRearmCount;
    private string? _failingCanonicalPath;
    private bool _disposed;
    private long _stateGeneration;

    public DirectoryWatcherService()
        : this(
            s_debounceInterval,
            callback => Dispatcher.UIThread.Post(callback, DispatcherPriority.Background))
    {
    }

    internal DirectoryWatcherService(
        TimeSpan debounceInterval,
        Action<Action> postDelivery,
        Action<FileSystemWatcher>? startWatcher = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounceInterval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(postDelivery);

        _debounceInterval = debounceInterval;
        _postDelivery = postDelivery;
        _startWatcher = startWatcher
                        ?? (static watcher => watcher.EnableRaisingEvents = true);
    }

    // ファイルシステムに変更があったときに発火する。UIスレッドで呼び出される。
    public event Action? Changed;

    internal bool IsWatching
    {
        get
        {
            lock (_stateSync)
            {
                return _watcher is not null;
            }
        }
    }

    // 指定パスの監視を開始する。前回の監視は自動的に停止される。
    public void Watch(string? path) => Watch(path, isErrorRearm: false);

    private void Watch(string? path, bool isErrorRearm)
    {
        FileSystemWatcher? currentWatcher;
        string? watchedCanonicalPath;
        lock (_stateSync)
        {
            if (_disposed)
                return;

            currentWatcher = _watcher;
            watchedCanonicalPath = _watchedCanonicalPath;
        }

        bool pathResolved = TryResolveWatchPath(path, out string? canonicalPath);

        // Only changing folders clears a failure budget.
        if (!isErrorRearm)
        {
            lock (_stateSync)
            {
                if (!pathResolved
                    || !string.Equals(
                        _failingCanonicalPath,
                        canonicalPath,
                        StringComparison.Ordinal))
                {
                    _errorRearmCount = 0;
                    _failingCanonicalPath = null;
                }
            }
        }

        // Recursive watchers consume one inotify descriptor per subdirectory.
        if (currentWatcher is not null
            && pathResolved
            && canonicalPath is not null
            && Directory.Exists(canonicalPath)
            && string.Equals(
                watchedCanonicalPath,
                canonicalPath,
                StringComparison.Ordinal))
        {
            lock (_stateSync)
            {
                if (!_disposed
                    && ReferenceEquals(_watcher, currentWatcher)
                    && string.Equals(
                        _watchedCanonicalPath,
                        canonicalPath,
                        StringComparison.Ordinal))
                {
                    _watchedRequestedPath = Path.GetFullPath(path!);
                    return;
                }
            }
        }

        CancellationTokenSource? previousDebounce;
        FileSystemWatcher? previousWatcher;
        long watchGeneration;
        lock (_stateSync)
        {
            if (_disposed)
                return;

            watchGeneration = ++_stateGeneration;
            previousDebounce = _debounceCts;
            _debounceCts = null;
            previousWatcher = _watcher;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
        }

        CancelAndDispose(previousDebounce);
        previousWatcher?.Dispose();
        _templateOrMaterialDirectories.Clear();
        _directoryIdentities.Clear();

        if (!pathResolved || canonicalPath is null || !Directory.Exists(canonicalPath))
            return;

        FileSystemWatcher? nextWatcher = null;
        try
        {
            nextWatcher = new FileSystemWatcher(canonicalPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
            };

            nextWatcher.Created += OnFileSystemEvent;
            nextWatcher.Deleted += OnFileSystemEvent;
            nextWatcher.Renamed += OnFileSystemEvent;
            nextWatcher.Changed += OnFileSystemEvent;
            nextWatcher.Error += OnWatcherError;

            lock (_stateSync)
            {
                if (_disposed || _stateGeneration != watchGeneration)
                    return;

                _watcher = nextWatcher;
                _watchedCanonicalPath = canonicalPath;
                _watchedRequestedPath = Path.GetFullPath(path!);
                try
                {
                    _startWatcher(nextWatcher);
                }
                catch
                {
                    _watcher = null;
                    _watchedCanonicalPath = null;
                    _watchedRequestedPath = null;
                    throw;
                }

                nextWatcher = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", canonicalPath);
        }
        finally
        {
            nextWatcher?.Dispose();
        }
    }

    // Rebuild after Error; otherwise the current folder stays unwatched.
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (!IsCurrentWatcher(sender))
            return;

        _logger.LogWarning(e.GetException(), "FileSystemWatcher stopped; rebuilding it");

        Dispatcher.UIThread.Post(() =>
        {
            lock (_stateSync)
            {
                if (!IsCurrentWatcher(sender))
                    return;

                if (TryRearmAfterError())
                {
                    // Resync changes missed while the watcher was down.
                    Changed?.Invoke();
                }
            }
        });
    }

    internal bool TryRearmAfterError()
    {
        string? requestedPath;
        string? canonicalPath;
        FileSystemWatcher? watcher;
        CancellationTokenSource? debounce;
        lock (_stateSync)
        {
            if (_disposed)
                return false;

            watcher = _watcher;
            requestedPath = _watchedRequestedPath ?? watcher?.Path;
            canonicalPath = _watchedCanonicalPath ?? watcher?.Path;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
            debounce = _debounceCts;
            _debounceCts = null;
            _stateGeneration++;
        }

        CancelAndDispose(debounce);
        watcher?.Dispose();
        _templateOrMaterialDirectories.Clear();
        _configuredSpecialDirectoryIdentities.Clear();
        _directoryIdentities.Clear();

        if (requestedPath is null || canonicalPath is null)
        {
            return false;
        }

        lock (_stateSync)
        {
            _failingCanonicalPath ??= canonicalPath;
        }

        // Watch swallows a construction failure, and with no watcher no further Error can arrive to
        // spend what is left of the budget.
        while (true)
        {
            lock (_stateSync)
            {
                if (_disposed || _errorRearmCount >= MaxErrorRearms)
                {
                    break;
                }

                _errorRearmCount++;
            }

            Watch(requestedPath, isErrorRearm: true);
            if (IsWatching)
                return true;
        }

        _logger.LogWarning(
            "FileSystemWatcher for {Path} could not be rebuilt in {Count} attempts; leaving it off until the folder changes",
            requestedPath,
            GetErrorRearmCount());
        return false;
    }

    private int GetErrorRearmCount()
    {
        lock (_stateSync)
        {
            return _errorRearmCount;
        }
    }

    private bool IsCurrentWatcher(object sender)
    {
        lock (_stateSync)
        {
            return !_disposed && ReferenceEquals(_watcher, sender);
        }
    }

    // プロジェクト、シーン、要素のファイルは頻繁に変更されるため除外
    internal bool ShouldExcludePath(string path)
    {
        return ShouldExcludePath(
            path,
            BeutlEnvironment.GetTemplatesDirectoryPath(),
            BeutlEnvironment.GetMaterialsDirectoryPath());
    }

    internal bool ShouldExcludePath(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        // Templates and materials live below BEUTL_HOME/.beutl by default, so their explicit
        // exception must win over the reserved-metadata rule. Cache by containing directory: a
        // watcher burst commonly reports hundreds of sibling files, and canonical resolution only
        // needs to run once for that directory identity.
        if (IsTemplateOrMaterialPath(
                path,
                templatesDirectoryPath,
                materialsDirectoryPath))
        {
            return false;
        }

        if (HasReservedMetadataSegment(path))
        {
            return true;
        }

        return path.EndsWith(".bep", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".belm", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTemplateOrMaterialPath(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        if (IsConfiguredSpecialDirectory(
                path,
                templatesDirectoryPath,
                materialsDirectoryPath))
        {
            return true;
        }

        string? directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string canonicalDirectory;
        string canonicalTemplatesDirectory;
        string canonicalMaterialsDirectory;
        try
        {
            canonicalDirectory = ResolveDirectoryIdentity(Path.GetFullPath(directory));
            canonicalTemplatesDirectory = ResolveConfiguredDirectoryIdentity(
                templatesDirectoryPath);
            canonicalMaterialsDirectory = ResolveConfiguredDirectoryIdentity(
                materialsDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }

        if (_templateOrMaterialDirectories.Count >= 512)
        {
            _templateOrMaterialDirectories.Clear();
            _directoryIdentities.Clear();
        }

        return _templateOrMaterialDirectories.GetOrAdd(
            new SpecialDirectoryMembershipKey(
                canonicalTemplatesDirectory,
                canonicalMaterialsDirectory,
                canonicalDirectory),
            static key => FilePathComparison.IsSameOrDescendant(
                              key.TemplatesDirectory,
                              key.CandidateDirectory)
                          || FilePathComparison.IsSameOrDescendant(
                              key.MaterialsDirectory,
                              key.CandidateDirectory));
    }

    private bool IsConfiguredSpecialDirectory(
        string path,
        string templatesDirectoryPath,
        string materialsDirectoryPath)
    {
        try
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string fullTemplatesDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                templatesDirectoryPath));
            string fullMaterialsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                materialsDirectoryPath));
            if (string.Equals(fullPath, fullTemplatesDirectory, StringComparison.Ordinal)
                || string.Equals(fullPath, fullMaterialsDirectory, StringComparison.Ordinal))
            {
                return true;
            }

            // Files cannot be either special directory. Resolve their containing directory
            // through the shared membership cache instead of enumerating every sibling file.
            if (!Directory.Exists(fullPath))
                return false;

            string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
            return string.Equals(
                       canonicalPath,
                       ResolveConfiguredDirectoryIdentity(templatesDirectoryPath),
                       StringComparison.Ordinal)
                   || string.Equals(
                       canonicalPath,
                       ResolveConfiguredDirectoryIdentity(materialsDirectoryPath),
                       StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            return false;
        }
    }

    private string ResolveConfiguredDirectoryIdentity(string directory)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(fullPath))
        {
            string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
            _configuredSpecialDirectoryIdentities[fullPath] = canonicalPath;
            return canonicalPath;
        }

        return _configuredSpecialDirectoryIdentities.TryGetValue(
            fullPath,
            out string? previousIdentity)
            ? previousIdentity
            : FilePathComparison.ResolveCanonicalPath(fullPath);
    }

    private string ResolveDirectoryIdentity(string fullPath)
    {
        string fingerprint = CreateLinkFingerprint(fullPath);
        if (_directoryIdentities.TryGetValue(fullPath, out DirectoryIdentity? identity)
            && string.Equals(identity.LinkFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return identity.CanonicalPath;
        }

        string canonicalPath = FilePathComparison.ResolveCanonicalPath(fullPath);
        _directoryIdentities[fullPath] = new DirectoryIdentity(fingerprint, canonicalPath);
        return canonicalPath;
    }

    private static string CreateLinkFingerprint(string directory)
    {
        string root = Path.GetPathRoot(directory)
                      ?? throw new ArgumentException(
                          "The directory has no filesystem root.",
                          nameof(directory));
        string current = root;
        var fingerprint = new StringBuilder();
        foreach (string segment in directory[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            string? linkTarget = info.LinkTarget;
            fingerprint.Append(segment)
                .Append('=')
                .Append(linkTarget);
            if (linkTarget is not null)
            {
                fingerprint.Append("->")
                    .Append(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName);
            }

            fingerprint
                .Append('\0');
        }

        return fingerprint.ToString();
    }

    private static bool HasReservedMetadataSegment(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
                      ?? throw new ArgumentException("The path has no filesystem root.", nameof(path));
        string parent = root;
        foreach (string segment in fullPath[root.Length..].Split(
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

    private static bool AreSameChildPath(string parent, string leftName, string rightName)
    {
        return FilePathComparison.TryAreSameChildPath(
                   parent,
                   leftName,
                   rightName,
                   out bool areSame)
               && areSame;
    }

    private bool TryResolveWatchPath(string? path, out string? canonicalPath)
    {
        canonicalPath = null;
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        try
        {
            canonicalPath = FilePathComparison.ResolveCanonicalPath(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve watched directory path {Path}",
                path);
            return false;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (IsCurrentWatcher(sender))
        {
            NotifyPathChanged(e.FullPath, sender);
        }
    }

    internal void NotifyPathChanged(string path) => NotifyPathChanged(path, sourceWatcher: null);

    private void NotifyPathChanged(string path, object? sourceWatcher)
    {
        try
        {
            if (ShouldExcludePath(path))
                return;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to classify filesystem event path {Path}", path);
            return;
        }

        var nextDebounce = new CancellationTokenSource();
        CancellationToken token = nextDebounce.Token;
        CancellationTokenSource? previousDebounce;
        long deliveryGeneration;
        lock (_stateSync)
        {
            if (_disposed
                || sourceWatcher is not null && !ReferenceEquals(_watcher, sourceWatcher))
            {
                nextDebounce.Dispose();
                return;
            }

            deliveryGeneration = ++_stateGeneration;
            previousDebounce = _debounceCts;
            _debounceCts = nextDebounce;
            _ = PostDeliveryAfterDelayAsync(nextDebounce, token, deliveryGeneration);
        }

        CancelAndDispose(previousDebounce);
    }

    private async Task PostDeliveryAfterDelayAsync(
        CancellationTokenSource debounce,
        CancellationToken token,
        long deliveryGeneration)
    {
        try
        {
            await Task.Delay(_debounceInterval, token).ConfigureAwait(false);
            _postDelivery(() => TryDeliver(debounce, deliveryGeneration));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ClearFailedDelivery(debounce, deliveryGeneration);
            _logger.LogWarning(ex, "Failed to deliver a file browser change notification");
        }
    }

    private void ClearFailedDelivery(
        CancellationTokenSource debounce,
        long deliveryGeneration)
    {
        bool ownsDebounce;
        lock (_stateSync)
        {
            ownsDebounce = deliveryGeneration == _stateGeneration
                           && ReferenceEquals(_debounceCts, debounce);
            if (ownsDebounce)
            {
                _debounceCts = null;
            }
        }

        if (ownsDebounce)
        {
            debounce.Dispose();
        }
    }

    private void TryDeliver(CancellationTokenSource debounce, long deliveryGeneration)
    {
        lock (_stateSync)
        {
            if (_disposed
                || deliveryGeneration != _stateGeneration
                || !ReferenceEquals(_debounceCts, debounce))
            {
                return;
            }

            _debounceCts = null;
            _errorRearmCount = 0;
            _failingCanonicalPath = null;
            debounce.Dispose();
            Changed?.Invoke();
        }
    }

    // A delivered event resets the rearm budget.
    internal void MarkDelivered()
    {
        lock (_stateSync)
        {
            if (_disposed)
                return;

            _errorRearmCount = 0;
            _failingCanonicalPath = null;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? debounce;
        FileSystemWatcher? watcher;
        lock (_stateSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _stateGeneration++;
            debounce = _debounceCts;
            _debounceCts = null;
            watcher = _watcher;
            _watcher = null;
            _watchedCanonicalPath = null;
            _watchedRequestedPath = null;
        }

        CancelAndDispose(debounce);
        watcher?.Dispose();
        _templateOrMaterialDirectories.Clear();
        _directoryIdentities.Clear();
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
