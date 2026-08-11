using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public enum InstalledMaterialKind
{
    Image,
    Audio,
    Video,
    Font,
    Other
}

/// <param name="PackageName">
/// The directory a material package unpacked into, which is the package id.
/// </param>
public sealed record InstalledMaterial(
    string Name,
    string FilePath,
    string PackageName,
    InstalledMaterialKind Kind);

/// <summary>
/// Lists the files material packages have installed under <c>{home}/materials</c>.
/// </summary>
/// <remarks>
/// Everything found is listed, whatever its extension: <see cref="InstalledMaterialKind"/>
/// only groups the list, and what a given file can be dropped onto is the drop target's
/// decision.
/// </remarks>
public sealed class InstalledMaterialService
{
    private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(300);

    private static readonly string[] s_imageExtensions =
        [".bmp", ".gif", ".ico", ".jpg", ".jpeg", ".png", ".webp", ".wbmp", ".avif", ".heif"];

    private static readonly string[] s_audioExtensions =
        [".wav", ".mp3", ".ogg", ".oga", ".flac", ".aac", ".m4a", ".opus"];

    private static readonly string[] s_videoExtensions =
        [".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv", ".mpg", ".mpeg"];

    private static readonly string[] s_fontExtensions = [".ttf", ".ttc", ".otf"];

    public static readonly InstalledMaterialService Instance = new();

    private readonly string _directoryPath = BeutlEnvironment.GetMaterialsDirectoryPath();
    private readonly ILogger _logger = Log.CreateLogger<InstalledMaterialService>();
    private readonly Lock _lock = new();
    private InstalledMaterial[] _items = [];
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;

    private InstalledMaterialService()
    {
        RestoreItems();
        StartWatching();
    }

    /// <remarks>
    /// Raised from a thread-pool thread, so a view has to marshal before it reacts.
    /// </remarks>
    public event EventHandler? Changed;

    public string DirectoryPath => _directoryPath;

    /// <summary>
    /// The materials found by the last scan. The array is never mutated, so a caller on
    /// any thread can hold on to it.
    /// </summary>
    public InstalledMaterial[] GetItems()
    {
        lock (_lock)
        {
            return _items;
        }
    }

    public void RestoreItems()
    {
        try
        {
            InstalledMaterial[] found = Scan(_directoryPath);

            lock (_lock)
            {
                _items = found;
            }

            _logger.LogInformation(
                "Found {Count} installed materials in {DirectoryPath}.", found.Length, _directoryPath);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring installed materials.");
        }
    }

    public static InstalledMaterialKind ClassifyByExtension(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        if (Contains(s_imageExtensions, extension)) return InstalledMaterialKind.Image;
        if (Contains(s_audioExtensions, extension)) return InstalledMaterialKind.Audio;
        if (Contains(s_videoExtensions, extension)) return InstalledMaterialKind.Video;
        if (Contains(s_fontExtensions, extension)) return InstalledMaterialKind.Font;
        return InstalledMaterialKind.Other;

        static bool Contains(string[] extensions, string extension)
        {
            return Array.Exists(extensions, x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static InstalledMaterial[] Scan(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                .Select(x => CreateItem(directoryPath, x))
                .OrderBy(x => x.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static InstalledMaterial CreateItem(string directoryPath, string filePath)
    {
        string relative = Path.GetRelativePath(directoryPath, filePath);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string packageName = segments.Length > 1 ? segments[0] : string.Empty;

        return new InstalledMaterial(
            Path.GetFileName(filePath), filePath, packageName, ClassifyByExtension(filePath));
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            _watcher = new FileSystemWatcher(_directoryPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;
            _watcher.Error += OnWatcherError;

            _logger.LogInformation("Started watching materials directory: {DirectoryPath}", _directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", _directoryPath);
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleRefresh();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The watcher lost events (e.g. its internal buffer overflowed), so the list
        // may be stale; rescan to resynchronize.
        _logger.LogWarning(e.GetException(), "The materials watcher reported an error; rescanning.");
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;

            Task.Delay(s_debounceInterval, token).ContinueWith(
                _ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        RestoreItems();
                    }
                },
                token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }
}
