using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor;

internal sealed class ProjectAssetCollection(string root)
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectAssetCollection>();
    private readonly ProjectAssetDirectory _root = ProjectAssetDirectory.OpenRoot(root);
    private readonly Dictionary<string, ProjectAssetDirectory> _stagingDirectories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProjectAssetDirectory> _publicationDirectories = new(StringComparer.Ordinal);
    private readonly List<(ProjectAssetDirectory Parent, ProjectAssetDirectory Child, string Name, bool Created)> _openedDirectories = [];
    private readonly List<StagedFile> _files = [];

    internal IReadOnlyList<StagedFile> Files => _files;

    internal async Task StageAsync(ProjectAssetFile file, CancellationToken token)
    {
        await using FileStream source = ResourceRelocationService.OpenCopySource(file.Path);
        ProjectAssetDirectory directory = OpenParent(file.Destination, create: true);
        string name = $".{Guid.NewGuid():N}.tmp";
        using SafeFileHandle handle = directory.CreateFile(name);
        _files.Add(new StagedFile(file, directory, name));

        await using var destination = new FileStream(handle, FileAccess.Write, 81920);
        await source.CopyToAsync(destination, token);
        destination.Flush(flushToDisk: true);
        if (destination.Length != file.Stamp!.Value.Length)
            throw new IOException($"The copied file has changed length: {file.Path}");
    }

    internal void Publish(StagedFile staged)
    {
        // Walk again from the same root. A changed destination parent must pass the same
        // link checks, and the staging file still comes from the original opened parent.
        ProjectAssetDirectory directory = OpenParent(staged.File.Destination, create: false);
        staged.HasStagingName = staged.Parent.PublishFile(staged.Name, directory, Path.GetFileName(staged.File.Destination));
        staged.PublishedParent = directory;
    }

    internal void Cleanup(bool preserveCopies)
    {
        try
        {
            foreach (StagedFile staged in _files)
            {
                if (staged.HasStagingName) TryDelete(staged.Parent, staged.Name, directory: false);
                if (!preserveCopies && staged.PublishedParent is { } published)
                    TryDelete(published, Path.GetFileName(staged.File.Destination), directory: false);
            }
        }
        finally
        {
            for (int i = _openedDirectories.Count - 1; i >= 0; i--)
            {
                var (parent, child, name, created) = _openedDirectories[i];
                child.Dispose();
                if (created && !preserveCopies) TryDelete(parent, name, directory: true);
            }
            _root.Dispose();
        }
    }

    private ProjectAssetDirectory OpenParent(string path, bool create)
    {
        if (!ProjectAssetPaths.IsBelow(root, path)) throw new IOException($"The path is outside the project: '{path}'.");
        string relative = Path.GetRelativePath(root, path);
        string[] components = relative.Split(Path.DirectorySeparatorChar);
        var directories = create ? _stagingDirectories : _publicationDirectories;
        ProjectAssetDirectory parent = _root;
        for (int i = 0; i < components.Length - 1; i++)
        {
            string name = components[i];
            string childPath = Path.Combine(parent.DisplayPath, name);
            if (!directories.TryGetValue(childPath, out ProjectAssetDirectory? child))
            {
                child = parent.OpenDirectory(name, create, out bool created);
                _openedDirectories.Add((parent, child, name, created));
                directories.Add(childPath, child);
            }
            parent = child;
        }
        return parent;
    }

    private static void TryDelete(ProjectAssetDirectory parent, string name, bool directory)
    {
        try
        {
            parent.Delete(name, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogWarning(ex, "Could not remove an uncommitted asset entry {Name} in {Directory}.", name, parent.DisplayPath);
        }
    }

    internal sealed class StagedFile(ProjectAssetFile file, ProjectAssetDirectory parent, string name)
    {
        internal ProjectAssetFile File { get; } = file;
        internal ProjectAssetDirectory Parent { get; } = parent;
        internal string Name { get; } = name;
        internal bool HasStagingName { get; set; } = true;
        internal ProjectAssetDirectory? PublishedParent { get; set; }
    }
}
