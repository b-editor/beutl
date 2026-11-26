using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor;

internal sealed class ProjectAssetCollection(string root)
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectAssetCollection>();
    private readonly ProjectAssetDirectory _root = ProjectAssetDirectory.OpenRoot(root);
    private readonly Dictionary<string, ProjectAssetDirectory> _stagingDirectories = new(StringComparer.Ordinal);
    private readonly List<(ProjectAssetDirectory Parent, ProjectAssetDirectory Child, string Name, bool Created)> _openedDirectories = [];
    private readonly List<StagedFile> _files = [];

    internal IReadOnlyList<StagedFile> Files => _files;

    internal async Task StageAsync(ProjectAssetFile file, CancellationToken token)
    {
        await using FileStream source = ResourceRelocationService.OpenCopySource(file.Path);
        ProjectAssetDirectory directory = OpenStagingParent(file.Destination);
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
        // Open the destination from the root for each file. The verified staging handle
        // can then serve for rollback.
        string parentPath = Path.GetDirectoryName(GetRelativePath(staged.File.Destination))!;
        ProjectAssetDirectory directory = _root.OpenExistingPath(parentPath);
        try
        {
            if (!directory.IsSameDirectory(staged.Parent)) throw new ProjectAssetPlanChangedException();
            staged.HasStagingName = staged.Parent.PublishFile(staged.Name, directory, Path.GetFileName(staged.File.Destination));
            staged.Published = true;
        }
        finally
        {
            if (directory != _root) directory.Dispose();
        }
    }

    internal void Cleanup(bool preserveCopies)
    {
        try
        {
            foreach (StagedFile staged in _files)
            {
                if (staged.HasStagingName) TryDelete(staged.Parent, staged.Name, directory: false);
                if (!preserveCopies && staged.Published)
                    TryDelete(staged.Parent, Path.GetFileName(staged.File.Destination), directory: false);
            }
        }
        finally
        {
            for (int i = _openedDirectories.Count - 1; i >= 0; i--)
            {
                var (parent, child, name, created) = _openedDirectories[i];
                bool remove = created && !preserveCopies && IsCurrentDirectory(parent, child, name);
                child.Dispose();
                if (remove) TryDelete(parent, name, directory: true);
            }
            _root.Dispose();
        }
    }

    private string GetRelativePath(string path)
    {
        if (!ProjectAssetPaths.IsBelow(root, path)) throw new IOException($"The path is outside the project: '{path}'.");
        return Path.GetRelativePath(root, path);
    }

    private ProjectAssetDirectory OpenStagingParent(string path)
    {
        string[] components = GetRelativePath(path).Split(Path.DirectorySeparatorChar);
        ProjectAssetDirectory parent = _root;
        for (int i = 0; i < components.Length - 1; i++)
        {
            string name = components[i];
            string childPath = Path.Combine(parent.DisplayPath, name);
            if (!_stagingDirectories.TryGetValue(childPath, out ProjectAssetDirectory? child))
            {
                child = parent.OpenDirectory(name, create: true, out bool created);
                _openedDirectories.Add((parent, child, name, created));
                _stagingDirectories.Add(childPath, child);
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

    private static bool IsCurrentDirectory(ProjectAssetDirectory parent, ProjectAssetDirectory opened, string name)
    {
        try
        {
            using ProjectAssetDirectory current = parent.OpenDirectory(name, create: false, out _);
            return current.IsSameDirectory(opened);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal sealed class StagedFile(ProjectAssetFile file, ProjectAssetDirectory parent, string name)
    {
        internal ProjectAssetFile File { get; } = file;
        internal ProjectAssetDirectory Parent { get; } = parent;
        internal string Name { get; } = name;
        internal bool HasStagingName { get; set; } = true;
        internal bool Published { get; set; }
    }
}
