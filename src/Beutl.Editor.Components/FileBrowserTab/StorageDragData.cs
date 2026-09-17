using Avalonia.Input;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.FileBrowserTab;

public sealed record StorageDragEntry(string Id, string Name, bool IsFolder);

// In-process only: native applications receive ordinary local file URLs, never account data.
public sealed record StorageDragData(string ProviderId, object AccountIdentity,
    IReadOnlyList<StorageDragEntry> Entries, IReadOnlyList<string> LocalPaths,
    Func<bool> IsCurrent, Func<string?, Task<bool>> MoveAsync)
{
    public static readonly DataFormat<StorageDragData> Format = DataFormat.CreateInProcessFormat<StorageDragData>("Beutl.StorageDrag");

    public async Task<IReadOnlyList<string>> ImportToSceneAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        if (!IsCurrent()) return [];
        string directory = Path.Combine(scene.Uri?.LocalPath is { } scenePath ? Path.GetDirectoryName(scenePath)! : Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "tmp", "unsaved", scene.Id.ToString("N")), "resources", "storage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var imported = new List<string>();
        try
        {
            foreach (string root in LocalPaths)
            {
                foreach (string source in Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : new[] { root })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrent()) throw new OperationCanceledException();
                    string relative = Directory.Exists(root) ? Path.Combine(Path.GetFileName(root), Path.GetRelativePath(root, source)) : Path.GetFileName(source);
                    string destination = Path.Combine(directory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using (var input = File.OpenRead(source))
                    await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await input.CopyToAsync(output, cancellationToken);
                    imported.Add(destination);
                }
            }
            return imported;
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }
}

public interface IFileBrowserStorageDropTarget
{
    bool CanDrop(IDataTransfer data, string? destination);
    Task DropAsync(IDataTransfer data, string? destination);
}
