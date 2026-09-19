using Avalonia.Input;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.FileBrowserTab;

public sealed record StorageDragEntry(string Id, string Name, bool IsFolder);

// In-process only: native applications receive ordinary local file URLs, never account data.
public sealed record StorageDragData(string ProviderId, object AccountIdentity,
    IReadOnlyList<StorageDragEntry> Entries, IReadOnlyList<string> LocalPaths,
    Func<bool> IsCurrent, Func<string?, Task<bool>> MoveAsync, IFileBrowserStorageBrowser? SourceBrowser = null)
{
    public static readonly DataFormat<StorageDragData> Format = DataFormat.CreateInProcessFormat<StorageDragData>("Beutl.StorageDrag");

    public Task<IReadOnlyList<string>>? PendingLocalPaths { get; init; }
    public Func<string?, bool>? CanMoveTo { get; init; }

    public async Task<StorageSceneImport> ImportToSceneAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        if (!IsCurrent()) return new StorageSceneImport(null, [], new Dictionary<string, string>());
        string directory = Path.Combine(scene.Uri?.LocalPath is { } scenePath ? Path.GetDirectoryName(scenePath)! : Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "unsaved", scene.Id.ToString("N")), "resources", "storage", Guid.NewGuid().ToString("N"));
        var localPaths = PendingLocalPaths != null ? await PendingLocalPaths.WaitAsync(cancellationToken) : LocalPaths;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent()) throw new OperationCanceledException();
        Directory.CreateDirectory(directory);
        var imported = new List<string>();
        var groups = new Dictionary<string, string>();
        try
        {
            foreach (string root in localPaths)
            {
                bool isFolder = Directory.Exists(root);
                string group = Path.Combine(directory, Path.GetFileName(root));
                foreach (string source in isFolder ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : new[] { root })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrent()) throw new OperationCanceledException();
                    string relative = isFolder ? Path.Combine(Path.GetFileName(root), Path.GetRelativePath(root, source)) : Path.GetFileName(source);
                    string destination = Path.Combine(directory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using (var input = File.OpenRead(source))
                    await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output, cancellationToken);
                    imported.Add(destination);
                    groups.Add(destination, group);
                }
            }
            return new StorageSceneImport(directory, imported, groups);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }
}

// Copied resources belong to this operation until a scene element takes ownership.
public sealed class StorageSceneImport : IDisposable
{
    private readonly string? _directory;
    private readonly IReadOnlyDictionary<string, string> _groups;
    private readonly HashSet<string> _retained = [];
    private bool _disposed;

    internal StorageSceneImport(string? directory, IReadOnlyList<string> paths, IReadOnlyDictionary<string, string> groups)
    {
        _directory = directory;
        Paths = paths;
        _groups = groups;
    }

    public IReadOnlyList<string> Paths { get; }

    public void Retain(string path)
    {
        // Keep sibling resources of an imported folder together: an accepted file may reference them.
        _retained.Add(_groups[path]);
    }

    public void RetainAll()
    {
        // Element templates can reference any of the accompanying resources.
        _retained.UnionWith(_groups.Values);
    }

    public void Dispose()
    {
        if (_disposed || _directory == null) return;
        _disposed = true;
        foreach (string group in _groups.Values.Distinct())
        {
            if (_retained.Contains(group)) continue;
            if (Directory.Exists(group)) Directory.Delete(group, true);
            else File.Delete(group);
        }
        if (Directory.Exists(_directory) && !Directory.EnumerateFileSystemEntries(_directory).Any())
            Directory.Delete(_directory);
    }
}

public interface IFileBrowserStorageDropTarget
{
    bool CanDrop(IDataTransfer data, string? destination);
    Task DropAsync(IDataTransfer data, string? destination);
}
