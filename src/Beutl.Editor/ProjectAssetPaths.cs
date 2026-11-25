using Beutl.Editor.VersionControl;

namespace Beutl.Editor;

internal sealed class ProjectAssetPaths(Project project)
{
    internal string ProjectFile { get; } = FilePathComparison.ResolveCanonicalPath(project.Uri!.LocalPath);
    internal string Root => Path.GetDirectoryName(ProjectFile)!;
    internal string Resources => Path.Combine(Root, "resources");
    internal string Trash => Path.Combine(Root, EditorConstants.BeutlFolder, "asset-trash", project.Id.Value.ToString("N"));

    internal bool IsInside(string path) => IsBelow(Root, path);

    internal bool IsWritable(string path)
    {
        if (!IsInside(path) || !FilePathComparison.IsSameOrDescendantCanonicalPath(Root, FilePathComparison.ResolveCanonicalPath(path)))
            return false;
        string relative = Path.GetRelativePath(Root, path);
        string current = Root;
        string[] components = relative.Split(Path.DirectorySeparatorChar);
        for (int index = 0; index < components.Length; index++)
        {
            current = Path.Combine(current, components[index]);
            FileAttributes? attributes = GetAttributes(current);
            if (attributes == null) break;
            if ((attributes.Value & FileAttributes.ReparsePoint) != 0) return false;
            if (index < components.Length - 1 && (attributes.Value & FileAttributes.Directory) == 0) return false;
        }

        return true;
    }

    internal void RequireWritable(string path)
    {
        if (!IsWritable(path)) throw new IOException($"The path is outside the project or passes through a link: {path}");
    }

    internal static bool IsBelow(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative != "." && relative != ".." && !Path.IsPathRooted(relative)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static FileAttributes? GetAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static IEnumerable<string> EnumerateFiles(string directory, CancellationToken token)
    {
        var pending = new Stack<string>();
        if (Directory.Exists(directory)) pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(current))
            {
                token.ThrowIfCancellationRequested();
                if (GetAttributes(path) is not { } attributes || attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!ProjectPathRules.IsTemporaryFile(path)) yield return path;
                    continue;
                }

                string name = Path.GetFileName(path);
                if (!ProjectPathRules.IsStateFolderName(name) && !name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    pending.Push(path);
            }
        }
    }
}
