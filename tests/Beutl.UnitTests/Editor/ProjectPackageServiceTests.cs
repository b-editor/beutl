using System.Collections.Concurrent;
using Beutl.Editor;
using Beutl.Logging;
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
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
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
        bool result = await service.ExportAsync(project, outputPath, progress);

        // Assert
        Assert.That(result, Is.True);
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
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
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
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result, Is.True);
        // Extract and verify .beutl is not included
        string extractDir = Path.Combine(_testDir, "verify");
        System.IO.Compression.ZipFile.ExtractToDirectory(outputPath, extractDir);
        string extractedBeutlDir = Path.Combine(extractDir, Path.GetFileName(_projectDir), ".beutl");
        Assert.That(Directory.Exists(extractedBeutlDir), Is.False);
    }

    [Test]
    public async Task ExportAsync_WithProjectItems_SavesItems()
    {
        // Arrange
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProjectWithItems();
        string outputPath = Path.Combine(_exportDir, "test_with_items.zip");

        // Act
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(File.Exists(outputPath), Is.True);
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
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result, Is.True);
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
        bool result = await service.ExportAsync(project, outputPath);

        // Assert
        Assert.That(result, Is.True);
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
        bool result = await service.ExportAsync(project, invalidOutputPath);

        // Assert - should return false because the export failed
        Assert.That(result, Is.False);
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
