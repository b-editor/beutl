using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class ProjectConflictMarkerScannerTests
{
    private string _root = null!;
    private string _outsideRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"beutl-conflict-scan-{Guid.NewGuid():N}");
        _outsideRoot = $"{_root}-outside";
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outsideRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        if (Directory.Exists(_outsideRoot))
        {
            Directory.Delete(_outsideRoot, recursive: true);
        }
    }

    [Test]
    public async Task FindFirstAsync_scans_only_project_file_types_for_conflict_markers()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string conflictFile = Path.Combine(_root, "elements", "conflict.belm");
        Directory.CreateDirectory(Path.GetDirectoryName(conflictFile)!);
        await File.WriteAllTextAsync(projectFile, "{\"value\":\"<<<<<<<without-space\"}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "ignored.txt"),
            "<<<<<<< conflict in an unrelated file\n");
        await File.WriteAllTextAsync(
            conflictFile,
            "<<<<<<< ours\n{\"value\":1}\n=======\n{\"value\":2}\n>>>>>>> theirs\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(conflictFile));
    }

    [Test]
    public async Task FindFirstAsync_returns_null_for_project_files_without_a_marker()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "scene.scene"),
            "{\"title\":\"safe\"}\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_prunes_metadata_directories_before_scanning_files()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string gitDirectory = Path.Combine(_root, ".git");
        string beutlDirectory = Path.Combine(_root, ".beutl");
        string resourcesDirectory = Path.Combine(_root, "resources");
        Directory.CreateDirectory(gitDirectory);
        Directory.CreateDirectory(beutlDirectory);
        Directory.CreateDirectory(resourcesDirectory);
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(gitDirectory, "conflict.scene"),
            "<<<<<<< metadata\n");
        await File.WriteAllTextAsync(
            Path.Combine(beutlDirectory, "conflict.belm"),
            "<<<<<<< metadata\n");
        await File.WriteAllTextAsync(
            Path.Combine(resourcesDirectory, "unrelated.scene"),
            "<<<<<<< imported resource\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(ProjectConflictMarkerScanner.ShouldDescendInto(gitDirectory), Is.False);
            Assert.That(ProjectConflictMarkerScanner.ShouldDescendInto(beutlDirectory), Is.False);
            Assert.That(ProjectConflictMarkerScanner.ShouldDescendInto(resourcesDirectory), Is.False);
        });
    }

    [Test]
    public async Task FindFirstAsync_rejects_a_project_file_symbolic_link_outside_the_project_root()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string outsideFile = Path.Combine(_outsideRoot, "outside.scene");
        string linkedFile = Path.Combine(_root, "linked.scene");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(outsideFile, "<<<<<<< external\n");
        try
        {
            File.CreateSymbolicLink(linkedFile, outsideFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating symbolic links is not supported in this environment.");
        }

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_does_not_descend_into_a_symbolic_link_directory()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string outsideFile = Path.Combine(_outsideRoot, "outside.scene");
        string linkedDirectory = Path.Combine(_root, "linked-assets");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(outsideFile, "<<<<<<< external\n");
        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, _outsideRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating symbolic links is not supported in this environment.");
        }

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(ProjectConflictMarkerScanner.ShouldDescendInto(linkedDirectory), Is.False);
        });
    }

    [Test]
    public async Task FindFirstAsync_finds_a_marker_across_a_scan_chunk_boundary()
    {
        const int scanChunkSize = 4096;
        string projectFile = Path.Combine(_root, "project.bep");
        string conflictFile = Path.Combine(_root, "boundary.scene");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            conflictFile,
            $"{new string('x', scanChunkSize - 3)}<<<<<<< ours\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(conflictFile));
    }

    [Test]
    public async Task FindFirstAsync_returns_null_for_a_large_single_line_without_a_marker()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string largeFile = Path.Combine(_root, "large.scene");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(largeFile, new string('x', 2 * 1024 * 1024));

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }
}
