using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.ProjectSystem;

// A directory for the matcher that leaves out the subdirectories in excluded, given as paths from this root with '/'
// between names and compared by the set's own comparer. The matcher never opens them and needs no exclude pattern for
// them; it would test each such pattern against every file.
internal sealed class FilteredDirectoryInfo(DirectoryInfoBase inner, IReadOnlySet<string> excluded, string? relativePath = null)
    : DirectoryInfoBase
{
    public override string Name => inner.Name;

    public override string FullName => inner.FullName;

    public override DirectoryInfoBase? ParentDirectory => inner.ParentDirectory;

    public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
    {
        foreach (FileSystemInfoBase info in inner.EnumerateFileSystemInfos())
        {
            if (info is not DirectoryInfoBase directory)
            {
                yield return info;
                continue;
            }

            string path = relativePath is null ? directory.Name : $"{relativePath}/{directory.Name}";
            if (!excluded.Contains(path))
            {
                yield return new FilteredDirectoryInfo(directory, excluded, path);
            }
        }
    }

    // The matcher asks only for "..", which no excluded path goes through.
    public override DirectoryInfoBase? GetDirectory(string path) => inner.GetDirectory(path);

    public override FileInfoBase? GetFile(string path) => inner.GetFile(path);
}
