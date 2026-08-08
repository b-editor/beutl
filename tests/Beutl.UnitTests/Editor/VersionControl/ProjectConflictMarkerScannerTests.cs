using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class ProjectConflictMarkerScannerTests
{
    private const string LfConflict = "<<<<<<< ours\n{\"value\":1}\n=======\n{\"value\":2}\n>>>>>>> theirs\n";

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
            LfConflict);
        await File.WriteAllTextAsync(conflictFile, LfConflict);

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
            LfConflict);
        await File.WriteAllTextAsync(
            Path.Combine(beutlDirectory, "conflict.belm"),
            LfConflict);
        await File.WriteAllTextAsync(
            Path.Combine(resourcesDirectory, "unrelated.scene"),
            LfConflict);

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
        await File.WriteAllTextAsync(outsideFile, LfConflict);
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
        await File.WriteAllTextAsync(outsideFile, LfConflict);
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
    public async Task FindFirstAsync_finds_a_complete_marker_triplet_across_a_scan_chunk_boundary()
    {
        const int scanChunkSize = 4096;
        string projectFile = Path.Combine(_root, "project.bep");
        string conflictFile = Path.Combine(_root, "boundary.scene");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            conflictFile,
            $"{new string('x', scanChunkSize - 4)}\n{LfConflict}");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(conflictFile));
    }

    [Test]
    public async Task FindFirstAsync_accepts_a_UTF8_BOM_before_the_first_marker()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string conflictFile = Path.Combine(_root, "bom.scene");
        byte[] markerBytes = System.Text.Encoding.UTF8.GetBytes(LfConflict);
        byte[] conflictBytes = new byte[3 + markerBytes.Length];
        conflictBytes[0] = 0xef;
        conflictBytes[1] = 0xbb;
        conflictBytes[2] = 0xbf;
        markerBytes.CopyTo(conflictBytes, 3);
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllBytesAsync(conflictFile, conflictBytes);

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(conflictFile));
    }

    [Test]
    public async Task FindFirstAsync_ignores_marker_text_inside_JSON_strings()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        await File.WriteAllTextAsync(
            projectFile,
            """
            {
              "literal": "prefix <<<<<<< ours ======= >>>>>>> theirs",
              "escaped": "<<<<<<<<< ours\n=========\n>>>>>>>>> theirs"
            }
            """);

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_requires_markers_at_line_boundaries()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        string sceneFile = Path.Combine(_root, "embedded.scene");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            sceneFile,
            "prefix <<<<<<< ours\n=======\n>>>>>>> theirs\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_requires_a_complete_ordered_marker_triplet()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "start-only.scene"),
            "<<<<<<< ours\n{\"value\":1}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "missing-separator.scene"),
            "<<<<<<< ours\n{\"value\":1}\n>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "missing-end.scene"),
            "<<<<<<< ours\n{\"value\":1}\n=======\n{\"value\":2}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "wrong-order.scene"),
            "<<<<<<< ours\n>>>>>>> theirs\n=======\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "invalid-separator.scene"),
            "<<<<<<< ours\n========\n>>>>>>> theirs\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_requires_the_same_marker_run_length()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "separator-mismatch.scene"),
            "<<<<<<<<< ours\n{\"value\":1}\n=======\n{\"value\":2}\n>>>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "end-mismatch.scene"),
            "<<<<<<<<< ours\n{\"value\":1}\n=========\n{\"value\":2}\n>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "start-mismatch.scene"),
            "<<<<<<< ours\n{\"value\":1}\n=========\n{\"value\":2}\n>>>>>>>>> theirs\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FindFirstAsync_requires_labels_on_start_and_end_marker_lines()
    {
        string projectFile = Path.Combine(_root, "project.bep");
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "missing-start-label.scene"),
            "<<<<<<< \n=======\n>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "missing-end-label.scene"),
            "<<<<<<< ours\n=======\n>>>>>>> \n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "whitespace-start-label.scene"),
            "<<<<<<<  \t \n=======\n>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "whitespace-end-label.scene"),
            "<<<<<<< ours\n=======\n>>>>>>> \t  \n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "unicode-whitespace-start-label.scene"),
            "<<<<<<< \u00a0\u3000\n=======\n>>>>>>> theirs\n");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "unicode-whitespace-end-label.scene"),
            "<<<<<<< ours\n=======\n>>>>>>> \u00a0\u3000\n");

        string? result = await ProjectConflictMarkerScanner.FindFirstAsync(
            projectFile,
            CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [TestCase(1, false)]
    [TestCase(7, false)]
    [TestCase(7, true)]
    [TestCase(9, false)]
    [TestCase(9, true)]
    public async Task FindFirstAsync_accepts_default_and_non_default_markers_at_file_boundaries(
        int markerSize,
        bool useCrLf)
    {
        string lineEnding = useCrLf ? "\r\n" : "\n";
        string projectFile = Path.Combine(_root, "project.bep");
        string conflictFile = Path.Combine(_root, "conflict.scene");
        string startMarker = new('<', markerSize);
        string separatorMarker = new('=', markerSize);
        string endMarker = new('>', markerSize);
        await File.WriteAllTextAsync(projectFile, "{}\n");
        await File.WriteAllTextAsync(
            conflictFile,
            $"{startMarker} ours{lineEnding}{{\"value\":1}}{lineEnding}{separatorMarker}{lineEnding}{{\"value\":2}}{lineEnding}{endMarker} theirs");

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
