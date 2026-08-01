using System.Collections.Concurrent;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class ProjectPackageServiceTests
{
    private string _testDir = null!;
    private string _projectDir = null!;
    private string _exportDir = null!;
    private string _importDir = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());

        _testDir = Path.Combine(Path.GetTempPath(), $"beutl_pkg_test_{Guid.NewGuid():N}");
        _projectDir = Path.Combine(_testDir, "project");
        _exportDir = Path.Combine(_testDir, "export");
        _importDir = Path.Combine(_testDir, "import");

        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_exportDir);
        Directory.CreateDirectory(_importDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Current Property Tests

    [Test]
    public void Current_ReturnsNonNullInstance()
    {
        // Act
        var service = ProjectPackageService.Current;

        // Assert
        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void Current_ReturnsSameInstance()
    {
        // Act
        var service1 = ProjectPackageService.Current;
        var service2 = ProjectPackageService.Current;

        // Assert
        Assert.That(service1, Is.SameAs(service2));
    }

    #endregion

    #region ExportAsync Tests

    [Test]
    public void ExportAsync_WithNullProject_ThrowsArgumentNullException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ExportAsync(null!, outputPath));
    }

    [Test]
    public void ExportAsync_WithNullOutputPath_ThrowsArgumentNullException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        var project = new Project();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ExportAsync(project, null!));
    }

    [Test]
    public void ExportAsync_WithUnsavedProject_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        var project = new Project(); // Uri is null
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExportAsync(project, outputPath));
    }

    [Test]
    public async Task ExportAsync_WithValidProject_ExportsSuccessfully()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(File.Exists(outputPath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        ConcurrentBag<double> progressValues = [];
        var progress = new Progress<(string Message, double Progress)>(p => progressValues.Add(p.Progress));

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath, progress);

        // Assert
        Assert.That(result.Success, Is.True);
        // Progress may or may not be reported depending on timing
    }

    [Test]
    public async Task ExportAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ExportAsync(project, outputPath, cancellationToken: cts.Token));
    }

    [Test]
    public async Task ExportAsync_WhenOutputFileExists_OverwritesFile()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Create existing file
        File.WriteAllText(outputPath, "dummy content");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            // File should be a valid ZIP now, not "dummy content"
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(13)); // "dummy content" length
        });
    }

    [Test]
    public async Task ExportAsync_ExcludesBeutlDirectory()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Create .beutl directory (should be excluded)
        string beutlDir = Path.Combine(_projectDir, ".beutl");
        Directory.CreateDirectory(beutlDir);
        File.WriteAllText(Path.Combine(beutlDir, "state.json"), "{}");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result.Success, Is.True);
        // Extract and verify .beutl is not included
        string extractDir = Path.Combine(_testDir, "verify");
        System.IO.Compression.ZipFile.ExtractToDirectory(outputPath, extractDir);
        string extractedBeutlDir = Path.Combine(extractDir, ".beutl");
        Assert.That(Directory.Exists(extractedBeutlDir), Is.False);
    }

    [Test]
    public async Task ExportImportAsync_ExcludesGitDirectoriesAndPreservesOtherDotfiles()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "git-filtering.zip");

        string gitDirectory = Path.Combine(_projectDir, ".git");
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        File.WriteAllText(Path.Combine(gitDirectory, "config"), "sensitive repository config");

        string nestedGitDirectory = Path.Combine(_projectDir, "assets", ".git");
        Directory.CreateDirectory(nestedGitDirectory);
        File.WriteAllText(Path.Combine(nestedGitDirectory, "HEAD"), "ref: refs/heads/main");

        string linkedWorktreeDirectory = Path.Combine(_projectDir, "linked-worktree");
        Directory.CreateDirectory(linkedWorktreeDirectory);
        File.WriteAllText(Path.Combine(linkedWorktreeDirectory, ".git"), "gitdir: ../.git/worktrees/linked");

        string beutlDirectory = Path.Combine(_projectDir, ".beutl");
        Directory.CreateDirectory(beutlDirectory);
        File.WriteAllText(Path.Combine(beutlDirectory, "state.json"), "{}");

        File.WriteAllText(Path.Combine(_projectDir, ".gitignore"), "*.tmp\n");
        string hiddenDirectory = Path.Combine(_projectDir, ".settings");
        Directory.CreateDirectory(hiddenDirectory);
        File.WriteAllText(Path.Combine(hiddenDirectory, "editor.json"), "{\"theme\":\"dark\"}");
        File.WriteAllText(Path.Combine(_projectDir, "assets", "clip.txt"), "project content");

        ExportResult exportResult = await service.ExportAsync(project, packagePath);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(exportResult.Success, Is.True);
        Assert.That(importedProject, Is.Not.Null);

        string importedProjectDirectory = Path.GetDirectoryName(importedProject!.Uri!.LocalPath)!;
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(importedProjectDirectory, ".git")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(importedProjectDirectory, "assets", ".git")), Is.False);
            Assert.That(File.Exists(Path.Combine(importedProjectDirectory, "linked-worktree", ".git")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(importedProjectDirectory, ".beutl")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(importedProjectDirectory, ".gitignore")),
                Is.EqualTo("*.tmp\n"));
            Assert.That(File.ReadAllText(Path.Combine(importedProjectDirectory, ".settings", "editor.json")),
                Is.EqualTo("{\"theme\":\"dark\"}"));
            Assert.That(File.ReadAllText(Path.Combine(importedProjectDirectory, "assets", "clip.txt")),
                Is.EqualTo("project content"));
        });
    }

    [Test]
    public async Task ExportAsync_AlwaysExcludesReservedMetadataRegardlessOfCasing()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "git-casing.zip");

        string upperGitDirectory = Path.Combine(_projectDir, ".GIT");
        Directory.CreateDirectory(upperGitDirectory);
        File.WriteAllText(Path.Combine(upperGitDirectory, "config"), "repository metadata");

        string worktreeDirectory = Path.Combine(_projectDir, "worktree");
        Directory.CreateDirectory(worktreeDirectory);
        string upperGitFile = Path.Combine(worktreeDirectory, ".GIT");
        File.WriteAllText(upperGitFile, "gitdir: ../.git/worktrees/example");

        string upperBeutlDirectory = Path.Combine(_projectDir, ".BEUTL");
        Directory.CreateDirectory(upperBeutlDirectory);
        File.WriteAllText(Path.Combine(upperBeutlDirectory, "state.json"), "{}");

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.That(result.Success, Is.True);
        using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry(".GIT/config"), Is.Null);
            Assert.That(archive.GetEntry("worktree/.GIT"), Is.Null);
            Assert.That(archive.GetEntry(".BEUTL/state.json"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_DoesNotFollowFileOrDirectorySymbolicLinks()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "symbolic-links.zip");

        string outsideDirectory = Path.Combine(_testDir, "outside");
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "secret.txt");
        File.WriteAllText(outsideFile, "external secret");
        string outsideAsset = Path.Combine(outsideDirectory, "external.scene");
        File.WriteAllText(outsideAsset, "external scene");

        string fileLink = Path.Combine(_projectDir, "linked-secret.txt");
        string directoryLink = Path.Combine(_projectDir, "linked-assets");
        try
        {
            File.CreateSymbolicLink(fileLink, outsideFile);
            Directory.CreateSymbolicLink(directoryLink, outsideDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating symbolic links is not supported in this environment.");
        }

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.That(result.Success, Is.True);
        using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry("linked-secret.txt"), Is.Null);
            Assert.That(archive.GetEntry("linked-assets/external.scene"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_WithReferencedFileSymbolicLink_RelocatesTarget()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-file-link");
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "referenced.png");
        File.WriteAllText(outsideFile, "referenced linked content");

        string referencedLink = Path.Combine(_projectDir, "referenced-link.png");
        string unreferencedLink = Path.Combine(_projectDir, "unreferenced-link.png");
        CreateFileSymbolicLinkOrIgnore(referencedLink, outsideFile);
        CreateFileSymbolicLinkOrIgnore(unreferencedLink, outsideFile);
        Project project = CreateAndSaveTestProjectWithImageSource(referencedLink);
        string packagePath = Path.Combine(_exportDir, "referenced-file-link.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(result.Success, Is.True);
        Assert.That(result.FailedResources, Is.Empty);
        Assert.That(importedProject, Is.Not.Null);
        string importedDirectory = Path.GetDirectoryName(importedProject!.Uri!.LocalPath)!;
        ImageSource importedSource = GetOnlyImageSource(importedProject);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(importedSource.Uri.LocalPath),
                Is.EqualTo("referenced linked content"));
            Assert.That(Path.GetDirectoryName(importedSource.Uri.LocalPath),
                Is.EqualTo(Path.Combine(importedDirectory, "resources")));
            Assert.That(File.Exists(Path.Combine(importedDirectory, "referenced-link.png")), Is.False);
            Assert.That(File.Exists(Path.Combine(importedDirectory, "unreferenced-link.png")), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithReferencedFileThroughDirectorySymbolicLink_RelocatesTarget()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-directory-link");
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "nested.png");
        File.WriteAllText(outsideFile, "referenced directory-link content");

        string linkedDirectory = Path.Combine(_projectDir, "linked-assets");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, outsideDirectory);
        string referencedFile = Path.Combine(linkedDirectory, "nested.png");
        Project project = CreateAndSaveTestProjectWithImageSource(referencedFile);
        string packagePath = Path.Combine(_exportDir, "referenced-directory-link.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(result.Success, Is.True);
        Assert.That(result.FailedResources, Is.Empty);
        Assert.That(importedProject, Is.Not.Null);
        string importedDirectory = Path.GetDirectoryName(importedProject!.Uri!.LocalPath)!;
        ImageSource importedSource = GetOnlyImageSource(importedProject);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(importedSource.Uri.LocalPath),
                Is.EqualTo("referenced directory-link content"));
            Assert.That(Path.GetDirectoryName(importedSource.Uri.LocalPath),
                Is.EqualTo(Path.Combine(importedDirectory, "resources")));
            Assert.That(Directory.Exists(Path.Combine(importedDirectory, "linked-assets")), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithReferencedCyclicSymbolicLink_ReportsPartialFailure()
    {
        var service = ProjectPackageService.Current;
        string firstLink = Path.Combine(_projectDir, "first.png");
        string secondLink = Path.Combine(_projectDir, "second.png");
        CreateFileSymbolicLinkOrIgnore(firstLink, secondLink);
        CreateFileSymbolicLinkOrIgnore(secondLink, firstLink);
        Project project = CreateAndSaveTestProjectWithImageSource(firstLink);
        string packagePath = Path.Combine(_exportDir, "referenced-link-cycle.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(NormalizeFailedFileIdentifier(result.FailedResources[0]), Is.EqualTo(firstLink));
            Assert.That(File.Exists(packagePath), Is.True);
        });

        using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry("first.png"), Is.Null);
            Assert.That(archive.GetEntry("second.png"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_WithReferencedBrokenSymbolicLink_ReportsPartialFailure()
    {
        var service = ProjectPackageService.Current;
        string brokenLink = Path.Combine(_projectDir, "broken.png");
        CreateFileSymbolicLinkOrIgnore(brokenLink, Path.Combine(_testDir, "missing.png"));
        Project project = CreateAndSaveTestProjectWithImageSource(brokenLink);
        string packagePath = Path.Combine(_exportDir, "referenced-broken-link.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(NormalizeFailedFileIdentifier(result.FailedResources[0]), Is.EqualTo(brokenLink));
            Assert.That(File.Exists(packagePath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithReferencedSymbolicLinkToDirectory_ReportsPartialFailure()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "directory-target");
        Directory.CreateDirectory(outsideDirectory);
        string directoryLink = Path.Combine(_projectDir, "linked-directory.png");
        CreateDirectorySymbolicLinkOrIgnore(directoryLink, outsideDirectory);
        Project project = CreateAndSaveTestProjectWithImageSource(directoryLink);
        string packagePath = Path.Combine(_exportDir, "referenced-directory-target.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(NormalizeFailedFileIdentifier(result.FailedResources[0]), Is.EqualTo(directoryLink));
            Assert.That(File.Exists(packagePath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithProjectItems_SavesItems()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProjectWithItems();
        string outputPath = Path.Combine(_exportDir, "test_with_items.zip");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(File.Exists(outputPath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithFileAndFontFailures_SurfacesBothInResult()
    {
        // Arrange: the stub returns canned failures from both file and font relocation
        // so we can verify ExportAsync concatenates them into ExportResult.FailedResources.
        var stub = new StubRelocationService(
            new RelocationResult(2, ["missing/file_a.png", "missing/file_b.png"]),
            new RelocationResult(1, ["MissingFamily1", "MissingFamily2"]));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "partial_failure.zip");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert: the ZIP was still written (partial success), and FailedResources
        // contains file failures first, then font failures, preserving order.
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(File.Exists(outputPath), Is.True);
            Assert.That(result.FailedResources, Is.EqualTo(new[]
            {
                "missing/file_a.png",
                "missing/file_b.png",
                "MissingFamily1",
                "MissingFamily2",
            }));
        });
    }

    [Test]
    public async Task ExportAsync_WithOnlyFileFailures_SurfacesFileFailures()
    {
        // Arrange
        var stub = new StubRelocationService(
            new RelocationResult(0, ["missing/only_file.png"]),
            new RelocationResult(0, []));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "file_only.zip");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "missing/only_file.png" }));
        });
    }

    [Test]
    public async Task ExportAsync_WithOnlyFontFailures_SurfacesFontFailures()
    {
        // Arrange
        var stub = new StubRelocationService(
            new RelocationResult(0, []),
            new RelocationResult(0, ["MissingFontFamily"]));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "font_only.zip");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "MissingFontFamily" }));
        });
    }

    [Test]
    public async Task ExportAsync_WhenZipCreationFails_PreservesAlreadyCollectedFailures()
    {
        // Arrange: file relocation accumulates failures, then the outer ZIP creation
        // step throws because the output path is a directory. The failures collected
        // before the abort must still be surfaced — otherwise we lose information
        // that's already in memory.
        var stub = new StubRelocationService(
            new RelocationResult(0, ["pre_abort_file.png"]),
            new RelocationResult(0, ["pre_abort_font"]));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string invalidOutputPath = Path.Combine(_exportDir, "invalid_output_dir");
        Directory.CreateDirectory(invalidOutputPath);

        // Act
        ExportResult result = await service.ExportAsync(project, invalidOutputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "pre_abort_file.png", "pre_abort_font" }));
        });
    }

    #endregion

    #region ImportAsync Tests

    [Test]
    public void ImportAsync_WithNullPackagePath_ThrowsArgumentNullException()
    {
        // Arrange
        var service = ProjectPackageService.Current;

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ImportAsync(null!, _importDir));
    }

    [Test]
    public void ImportAsync_WithNullDestinationDirectory_ThrowsArgumentNullException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        string packagePath = Path.Combine(_exportDir, "test.zip");

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ImportAsync(packagePath, null!));
    }

    [Test]
    public void ImportAsync_WithNonExistentPackage_ThrowsFileNotFoundException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        string packagePath = Path.Combine(_exportDir, "nonexistent.zip");

        // Act & Assert
        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await service.ImportAsync(packagePath, _importDir));
    }

    [Test]
    public async Task ImportAsync_WithValidPackage_ImportsSuccessfully()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert
        Assert.That(importedProject, Is.Not.Null);
    }

    [TestCase(".git/config")]
    [TestCase(".git/")]
    [TestCase("nested/.GIT/config")]
    [TestCase(".GiT\\config")]
    [TestCase("nested\\.GiT/config")]
    [TestCase(".git./config")]
    [TestCase("nested/.GiT /config")]
    [TestCase(".git")]
    [TestCase(".git.")]
    [TestCase(".git ")]
    [TestCase(".git::$DATA")]
    [TestCase(".GIT:stream")]
    [TestCase(".GiT.:payload")]
    [TestCase("nested\\.GIT:payload")]
    [TestCase("linked-worktree/.git")]
    [TestCase("linked-worktree\\.GIT")]
    public async Task ImportAsync_WithGitMetadataEntry_RejectsPackageBeforeExtraction(string entryName)
    {
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "unsafe-git-metadata.zip");
        await service.ExportAsync(originalProject, packagePath);
        AddArchiveEntry(packagePath, entryName, "malicious repository metadata");

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.Multiple(() =>
        {
            Assert.That(importedProject, Is.Null);
            Assert.That(Directory.Exists(Path.Combine(_importDir, "unsafe-git-metadata")), Is.False);
        });
    }

    [Test]
    public async Task ImportAsync_WithGitLikeEntryNames_ImportsSuccessfully()
    {
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "safe-git-like-names.zip");
        await service.ExportAsync(originalProject, packagePath);
        AddArchiveEntry(packagePath, ".gitignore", "*.tmp\n");
        AddArchiveEntry(packagePath, ".github/workflows/check.yml", "name: check\n");
        AddArchiveEntry(packagePath, "assets/project.git/config", "ordinary project data\n");

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(importedProject, Is.Not.Null);
        string importedDirectory = Path.GetDirectoryName(importedProject!.Uri!.LocalPath)!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(importedDirectory, ".gitignore")), Is.True);
            Assert.That(File.Exists(Path.Combine(importedDirectory, ".github", "workflows", "check.yml")), Is.True);
            Assert.That(File.Exists(Path.Combine(importedDirectory, "assets", "project.git", "config")), Is.True);
        });
    }

    [Test]
    public async Task ImportAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        List<double> progressValues = [];
        var progress = new Progress<(string Message, double Progress)>(p => progressValues.Add(p.Progress));

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir, progress);

        // Assert
        Assert.That(importedProject, Is.Not.Null);
    }

    [Test]
    public async Task ImportAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act & Assert
        var ex = Assert.CatchAsync<Exception>(async () =>
            await service.ImportAsync(packagePath, _importDir, cancellationToken: cts.Token));
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ImportAsync_WhenDestinationExists_CreatesUniqueDirectory()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        // Create existing directory with same name
        string existingDir = Path.Combine(_importDir, "test");
        Directory.CreateDirectory(existingDir);

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert
        Assert.That(importedProject, Is.Not.Null);
        // Should create test_1 or similar
    }

    [Test]
    public async Task ImportAsync_WithPackageWithoutProjectFile_ReturnsNull()
    {
        // Arrange
        var service = ProjectPackageService.Current;

        // Create a ZIP without project file
        string tempDir = Path.Combine(_testDir, "noproject");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "dummy.txt"), "content");
        string packagePath = Path.Combine(_exportDir, "noproject.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, packagePath);

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert
        Assert.That(importedProject, Is.Null);
    }

    #endregion

    #region GetUniqueDirectoryPath Tests (tested indirectly)

    [Test]
    public async Task ImportAsync_WithMultipleExistingDirectories_IncrementsCounter()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        // Create existing directories
        Directory.CreateDirectory(Path.Combine(_importDir, "test"));
        Directory.CreateDirectory(Path.Combine(_importDir, "test_1"));
        Directory.CreateDirectory(Path.Combine(_importDir, "test_2"));

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert
        Assert.That(importedProject, Is.Not.Null);
    }

    #endregion

    #region CopyDirectoryAsync Edge Cases

    [Test]
    public async Task ExportAsync_WithNestedDirectories_CopiesAllDirectories()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Create nested directories
        string nestedDir = Path.Combine(_projectDir, "assets", "images");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "image.txt"), "image data");

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ExportAsync_WithEmptySubDirectories_ExportsSuccessfully()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        // Create empty subdirectory
        Directory.CreateDirectory(Path.Combine(_projectDir, "empty_dir"));

        // Act
        ExportResult result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ImportAsync_WithPackageWithoutProjectFile_CleansUpExtractedDirectory()
    {
        // Arrange
        var service = ProjectPackageService.Current;

        // Create a ZIP without project file
        string tempDir = Path.Combine(_testDir, "noprojectcleanup");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "dummy.txt"), "content");
        string packagePath = Path.Combine(_exportDir, "noprojectcleanup.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, packagePath);

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert
        Assert.That(importedProject, Is.Null);
        // The extracted directory should have been cleaned up
        string expectedDir = Path.Combine(_importDir, "noprojectcleanup");
        Assert.That(Directory.Exists(expectedDir), Is.False);
    }

    [Test]
    public async Task ImportAsync_WithInvalidProjectFile_CleansUpExtractedDirectory()
    {
        // Arrange
        var service = ProjectPackageService.Current;

        // Create a ZIP with an invalid .bep file
        string tempDir = Path.Combine(_testDir, "invalidbep");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.bep"), "this is not valid bep content");
        string packagePath = Path.Combine(_exportDir, "invalidbep.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, packagePath);

        // Act
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert - should return null because RestoreFromUri fails
        Assert.That(importedProject, Is.Null);
        // The extracted directory should have been cleaned up
        string expectedDir = Path.Combine(_importDir, "invalidbep");
        Assert.That(Directory.Exists(expectedDir), Is.False);
    }

    #endregion

    #region Error Handling

    [Test]
    public async Task ExportAsync_WhenOutputPathIsDirectory_ReturnsFalse()
    {
        // Arrange - Use a directory as output path to cause ZipFile.CreateFromDirectory to fail
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();

        // Create a directory at the output path - this will cause the ZIP creation to fail
        string invalidOutputPath = Path.Combine(_exportDir, "invalid_output");
        Directory.CreateDirectory(invalidOutputPath);

        // Act
        ExportResult result = await service.ExportAsync(project, invalidOutputPath);

        // Assert - should return false because the export failed
        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task ImportAsync_WhenPackageIsCorrupt_ReturnsNull()
    {
        // Arrange
        var service = ProjectPackageService.Current;

        // Create a corrupt ZIP file (just some text, not a valid ZIP)
        string corruptPackagePath = Path.Combine(_exportDir, "corrupt.zip");
        File.WriteAllText(corruptPackagePath, "This is not a valid ZIP file");

        // Act
        Project? result = await service.ImportAsync(corruptPackagePath, _importDir);

        // Assert - should return null because the import failed
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Helper Methods

    private static void AddArchiveEntry(string packagePath, string entryName, string content)
    {
        using System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(
            packagePath,
            System.IO.Compression.ZipArchiveMode.Update);
        System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void CreateFileSymbolicLinkOrIgnore(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating file symbolic links is not supported in this environment.");
        }
    }

    private static void CreateDirectorySymbolicLinkOrIgnore(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating directory symbolic links is not supported in this environment.");
        }
    }

    private static string NormalizeFailedFileIdentifier(string identifier)
    {
        return Uri.TryCreate(identifier, UriKind.Absolute, out Uri? uri) && uri.IsFile
            ? uri.LocalPath
            : identifier;
    }

    private Project CreateAndSaveTestProject()
    {
        string projectFilePath = Path.Combine(_projectDir, "test.bep");
        var project = new Project();
        Uri projectUri = new(projectFilePath);
        project.Name = "TestProject";

        // Save the project using CoreSerializer
        CoreSerializer.StoreToUri(project, projectUri);

        // Restore the project to get a proper Uri set
        return CoreSerializer.RestoreFromUri<Project>(projectUri);
    }

    private Project CreateAndSaveTestProjectWithImageSource(string sourcePath)
    {
        string projectFilePath = Path.Combine(_projectDir, "linked-source.bep");
        var project = new Project { Name = "LinkedSourceProject" };
        var scene = new Scene(1920, 1080, "LinkedSourceScene")
        {
            Uri = new Uri(Path.Combine(_projectDir, "linked-source.scene")),
        };
        var element = new Element
        {
            Uri = new Uri(Path.Combine(_projectDir, "linked-source.belm")),
            Length = TimeSpan.FromSeconds(1),
        };
        var imageSource = new ImageSource();
        imageSource.ReadFrom(new Uri(sourcePath));
        element.AddObject(new SourceImage { Source = { CurrentValue = imageSource } });
        scene.Children.Add(element);
        project.Items.Add(scene);

        Uri projectUri = new(projectFilePath);
        CoreSerializer.StoreToUri(project, projectUri);
        return CoreSerializer.RestoreFromUri<Project>(projectUri);
    }

    private static ImageSource GetOnlyImageSource(Project project)
    {
        Scene scene = project.Items.OfType<Scene>().Single();
        SourceImage drawable = scene.Children.Single().Objects.OfType<SourceImage>().Single();
        return drawable.Source.CurrentValue!;
    }

    private sealed class StubRelocationService(
        RelocationResult fileResult,
        RelocationResult fontResult) : ResourceRelocationService
    {
        public override Task<RelocationResult> RelocateFileSourcesAsync(
            IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> sources,
            Project stagingProject,
            string projectDirectory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(fileResult);

        public override Task<RelocationResult> RelocateFontsAsync(
            IEnumerable<FontFamily> fontFamilies,
            string projectDirectory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(fontResult);
    }

    private Project CreateAndSaveTestProjectWithItems()
    {
        string projectFilePath = Path.Combine(_projectDir, "test_with_items.bep");
        var project = new Project();
        Uri projectUri = new(projectFilePath);
        project.Name = "TestProjectWithItems";

        // Create a Scene and save it
        var scene = new Scene(1920, 1080, "TestScene");
        string sceneFilePath = Path.Combine(_projectDir, "test_scene.scene");
        Uri sceneUri = new(sceneFilePath);
        CoreSerializer.StoreToUri(scene, sceneUri);

        // Restore the scene to get a proper Uri set
        scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        // Add the scene to the project
        project.Items.Add(scene);

        // Save the project
        CoreSerializer.StoreToUri(project, projectUri);

        // Restore the project to get a proper Uri set
        return CoreSerializer.RestoreFromUri<Project>(projectUri);
    }

    #endregion
}
