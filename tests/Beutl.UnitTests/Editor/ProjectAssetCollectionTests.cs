using Beutl.Editor;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class ProjectAssetCollectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Rollback_targets_the_directory_used_for_staging_or_publication(bool publish)
    {
        using var temporary = new TemporaryDirectory();
        string root = FilePathComparison.ResolveCanonicalPath(temporary.Path);
        string resources = Path.Combine(root, "resources");
        string renamed = Path.Combine(root, "previous-resources");
        Directory.CreateDirectory(resources);
        ProjectAssetFile file = CreateCopy(root, resources);

        var copies = new ProjectAssetCollection(root);
        try
        {
            await copies.StageAsync(file, CancellationToken.None);
            if (publish)
            {
                copies.Publish(copies.Files.Single());
                Assert.That(File.ReadAllText(file.Destination), Is.EqualTo("original"));
            }

            Directory.Move(resources, renamed);
            Directory.CreateDirectory(resources);
            File.WriteAllText(file.Destination, "replacement");
        }
        finally
        {
            copies.Cleanup(preserveCopies: false);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Directory.EnumerateFileSystemEntries(renamed), Is.Empty);
            Assert.That(File.ReadAllText(file.Destination), Is.EqualTo("replacement"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Each_publication_checks_for_a_changed_destination_parent(bool replaceDirectory)
    {
        using var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Path);
        string root = FilePathComparison.ResolveCanonicalPath(temporary.Path);
        string resources = Path.Combine(root, "resources");
        string renamed = Path.Combine(root, "previous-resources");
        ProjectAssetFile first = CreateCopy(root, resources);
        ProjectAssetFile second = first with { Destination = Path.Combine(resources, "other-asset.txt") };

        var copies = new ProjectAssetCollection(root);
        try
        {
            await copies.StageAsync(first, CancellationToken.None);
            await copies.StageAsync(second, CancellationToken.None);
            copies.Publish(copies.Files[0]);
            Directory.Move(resources, renamed);
            if (replaceDirectory) Directory.CreateDirectory(resources);

            Assert.Catch<IOException>(() => copies.Publish(copies.Files[1]));
        }
        finally
        {
            copies.Cleanup(preserveCopies: false);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Directory.EnumerateFileSystemEntries(renamed), Is.Empty);
            Assert.That(File.Exists(second.Destination), Is.False);
            Assert.That(Directory.Exists(resources), Is.EqualTo(replaceDirectory));
        });
    }

    [Test]
    public async Task Publication_refuses_to_overwrite_a_file_added_during_collection()
    {
        using var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(temporary.Path);
        string root = FilePathComparison.ResolveCanonicalPath(temporary.Path);
        string resources = Path.Combine(root, "resources");
        ProjectAssetFile file = CreateCopy(root, resources);

        var copies = new ProjectAssetCollection(root);
        try
        {
            await copies.StageAsync(file, CancellationToken.None);
            File.WriteAllText(file.Destination, "existing");
            Assert.Throws<IOException>(() => copies.Publish(copies.Files.Single()));
        }
        finally
        {
            copies.Cleanup(preserveCopies: false);
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(file.Destination), Is.EqualTo("existing"));
            Assert.That(Directory.EnumerateFileSystemEntries(resources), Is.EqualTo(new[] { file.Destination }));
        });
    }

    [Test]
    public void Opening_a_child_directory_refuses_symbolic_links()
    {
        using var temporary = new TemporaryDirectory();
        string project = Path.Combine(temporary.Path, "project");
        string other = Path.Combine(temporary.Path, "other");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(other);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(project, "alias"), other);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is (IOException or UnauthorizedAccessException))
        {
            Assert.Ignore("Creating symbolic links requires Windows Developer Mode or elevated privileges.");
        }

        using var root = ProjectAssetDirectory.OpenRoot(FilePathComparison.ResolveCanonicalPath(project));
        Assert.Throws<IOException>(() =>
        {
            using var link = root.OpenDirectory("alias", create: true, out _);
        });
    }

    private static ProjectAssetFile CreateCopy(string root, string resources)
    {
        string source = Path.Combine(root, "original.txt");
        File.WriteAllText(source, "original");
        return new ProjectAssetFile(source, Path.Combine(resources, "asset.txt"), ProjectAssetStamp.Read(source));
    }
}
