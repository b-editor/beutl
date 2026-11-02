using Beutl.ProjectSystem;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class FilteredDirectoryInfoTests
{
    private static readonly string s_root = Path.Combine(Path.GetTempPath(), "beutl-filtered-directory");

    private static readonly string[] s_files =
        ["top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "x/a/b/four.belm"];

    // The matcher finds nothing below an excluded directory and never opens it. A path only names a directory.
    [TestCase("a", new[] { "top.belm", "x/a/b/four.belm" }, new[] { ".", "x", "x/a", "x/a/b" })]
    [TestCase("A/B", new[] { "top.belm", "a/one.belm", "x/a/b/four.belm" }, new[] { ".", "a", "x", "x/a", "x/a/b" })]
    [TestCase("a/b/c", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "x/a/b/four.belm" }, new[] { ".", "a", "a/b", "x", "x/a", "x/a/b" })]
    [TestCase("top.belm", new[] { "top.belm", "a/one.belm", "a/b/two.belm", "a/b/c/three.belm", "x/a/b/four.belm" }, new[] { ".", "a", "a/b", "a/b/c", "x", "x/a", "x/a/b" })]
    public void An_excluded_directory_is_neither_matched_nor_opened(string excluded, string[] expectedFiles, string[] expectedOpened)
    {
        var opened = new List<string>();
        var matcher = new Matcher();
        matcher.AddInclude("**/*.belm");

        PatternMatchingResult result = matcher.Execute(new FilteredDirectoryInfo(
            new RecordingDirectory(new InMemoryDirectoryInfo(s_root, s_files), opened),
            new HashSet<string>([excluded], StringComparer.OrdinalIgnoreCase)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Files.Select(file => file.Path), Is.EquivalentTo(expectedFiles));
            Assert.That(opened, Is.EquivalentTo(expectedOpened));
        });
    }

    private sealed class RecordingDirectory(DirectoryInfoBase inner, List<string> opened) : DirectoryInfoBase
    {
        public override string Name => inner.Name;

        public override string FullName => inner.FullName;

        public override DirectoryInfoBase? ParentDirectory => inner.ParentDirectory;

        public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
        {
            opened.Add(Path.GetRelativePath(s_root, FullName).Replace('\\', '/'));
            return inner.EnumerateFileSystemInfos()
                .Select(info => info is DirectoryInfoBase directory ? new RecordingDirectory(directory, opened) : info);
        }

        public override DirectoryInfoBase? GetDirectory(string path) => inner.GetDirectory(path);

        public override FileInfoBase? GetFile(string path) => inner.GetFile(path);
    }
}
