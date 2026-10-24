using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class PathPolicyTests
{
    [Test]
    public void ToFileUri_PreservesLiteralNamesAndDirectoryBases()
    {
        foreach (string name in new[] { "a%41.png", "ア#50%20off.png", "ends with a space " })
        {
            string relative = Path.Combine("beutl path policy", name);
            string absolute = Path.GetFullPath(relative);
            Uri uri = PathPolicy.ToFileUri(relative);

            Assert.Multiple(() =>
            {
                Assert.That(uri.IsFile, Is.True);
                Assert.That(uri.LocalPath, Is.EqualTo(absolute));
                Assert.That(DecoderRegistry.ToFileUri(relative).AbsoluteUri, Is.EqualTo(uri.AbsoluteUri));
                Assert.That(UriHelper.CreateFromPath(absolute).AbsoluteUri, Is.EqualTo(uri.AbsoluteUri));
            });
        }

        string directory = Path.GetFullPath("beutl path policy") + Path.DirectorySeparatorChar;
        Uri baseUri = PathPolicy.ToFileUri(directory);
        Assert.That(new Uri(baseUri, "a%2541.png").LocalPath, Is.EqualTo(Path.Combine(directory, "a%41.png")));
    }

    [Test]
    public void LexicalPaths_PreserveCaseAndKeepCanonicalChecksExplicit()
    {
        string parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "Project");
        string outside = Path.Combine(parent, "outside");
        string link = Path.Combine(root, "link");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
            string escaped = Path.Combine(link, "new");
            string alternate = Path.Combine(parent, "project");
            string proxy = Path.Combine(root, new string('a', 64), "quarter.mp4");

            Assert.Multiple(() =>
            {
                Assert.That(PathPolicy.NormalizeLexicalPath(Path.Combine(root, "missing", "..")), Is.EqualTo(root));
                Assert.That(PathPolicy.AreSameLexicalPath(root, root + Path.DirectorySeparatorChar), Is.True);
                Assert.That(PathPolicy.AreSameLexicalPath(root, alternate), Is.False);
                Assert.That(FilePathComparison.AreSameCanonicalPath(root, alternate), Is.EqualTo(Directory.Exists(alternate)));
                Assert.That(PathPolicy.IsSameOrDescendantLexicalPath(root, root), Is.True);
                Assert.That(PathPolicy.IsSameOrDescendantLexicalPath(root, root + "-other"), Is.False);
                Assert.That(PathPolicy.IsSameOrDescendantLexicalPath(Path.GetPathRoot(root)!, root), Is.True);
                Assert.That(PathPolicy.IsSameOrDescendantLexicalPath(root, escaped), Is.True);
                Assert.That(FilePathComparison.IsSameOrDescendant(root, escaped), Is.False);
                Assert.That(PathPolicy.AreSameLexicalPath(root, Path.Combine(link, "..")), Is.True);
                Assert.That(FilePathComparison.AreSameCanonicalPath(root, Path.Combine(link, "..")), Is.False);
                Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(root, proxy), Is.True);
                Assert.That(ProxyPathUtilities.IsGeneratedProxyFinalPath(alternate, proxy), Is.False);
                Assert.That(FileCopyService.CopyDirectoryRecursive(outside, escaped, root,
                    (_, _) => Assert.Fail("A copy must not follow the destination link."), CancellationToken.None), Is.EqualTo(1));
            });
            Assert.That(Directory.Exists(Path.Combine(outside, "new")), Is.False);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating symlinks requires Windows developer mode or elevation.");
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }
}
