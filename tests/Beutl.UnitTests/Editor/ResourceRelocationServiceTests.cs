using Beutl.Editor;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class ResourceRelocationServiceTests
{
    private string _testDir = null!;
    private string _projectDir = null!;

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());

        _testDir = Path.Combine(Path.GetTempPath(), $"beutl_reloc_test_{Guid.NewGuid():N}");
        _projectDir = Path.Combine(_testDir, "project");
        Directory.CreateDirectory(_projectDir);
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

    [Test]
    public async Task RelocateFileSourcesAsync_WithMissingSourceFile_ReportsFailedItem()
    {
        // Arrange
        var service = new ResourceRelocationService();
        var project = new Project();
        string missingPath = Path.Combine(_testDir, "does_not_exist.png");
        Uri missingUri = new(missingPath);
        var sources = new[] { (Guid.NewGuid(), "Source", missingUri) };

        // Act
        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Has.Count.EqualTo(1));
            Assert.That(result.FailedItems[0], Does.Contain("does_not_exist.png"));
        });
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithExistingFileButUnknownObject_ReportsFailedItem()
    {
        // Arrange: file exists, but the GUID does not resolve in the project, so UpdateUri throws.
        var service = new ResourceRelocationService();
        var project = new Project();
        string sourceFilePath = Path.Combine(_testDir, "asset.bin");
        await File.WriteAllBytesAsync(sourceFilePath, new byte[] { 1, 2, 3 });
        Uri sourceUri = new(sourceFilePath);
        var sources = new[] { (Guid.NewGuid(), "NonExistentProperty", sourceUri) };

        // Act
        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Has.Count.EqualTo(1));
            Assert.That(result.FailedItems[0], Does.Contain("asset.bin"));
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithFailingFontFinder_ReportsFailedItem()
    {
        // Arrange: font finder throws for a specific family name.
        var service = new ResourceRelocationService(name =>
        {
            if (name == "BrokenFont")
                throw new InvalidOperationException("simulated finder failure");
            return [];
        });
        var fonts = new[] { new FontFamily("BrokenFont") };

        // Act
        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Has.Count.EqualTo(1));
            Assert.That(result.FailedItems[0], Is.EqualTo("BrokenFont"));
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithNoMatchingFontFiles_ReportsFailedItem()
    {
        // Arrange: font finder returns an empty enumerable for the family (font not installed).
        var service = new ResourceRelocationService(_ => []);
        var fonts = new[] { new FontFamily("UnknownFamily") };

        // Act
        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Has.Count.EqualTo(1));
            Assert.That(result.FailedItems[0], Is.EqualTo("UnknownFamily"));
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithMissingFontFile_ReportsFailedItem()
    {
        // Arrange: font finder returns a path that does not exist; CopyFileAsync throws.
        string missingFontPath = Path.Combine(_testDir, "missing.ttf");
        var service = new ResourceRelocationService(name =>
            name == "FamilyWithMissingFile" ? new[] { missingFontPath } : []);
        var fonts = new[] { new FontFamily("FamilyWithMissingFile") };

        // Act
        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Has.Count.EqualTo(1));
            Assert.That(result.FailedItems[0], Is.EqualTo("FamilyWithMissingFile"));
        });
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithNoSources_ReturnsEmptyResult()
    {
        // Arrange
        var service = new ResourceRelocationService();
        var project = new Project();

        // Act
        RelocationResult result = await service.RelocateFileSourcesAsync([], project, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Is.Empty);
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithNoFamilies_ReturnsEmptyResult()
    {
        // Arrange
        var service = new ResourceRelocationService(_ => []);

        // Act
        RelocationResult result = await service.RelocateFontsAsync([], _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedItems, Is.Empty);
        });
    }
}
