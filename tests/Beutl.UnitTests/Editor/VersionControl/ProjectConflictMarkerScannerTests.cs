using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class ProjectConflictMarkerScannerTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"beutl-conflict-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
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
}
