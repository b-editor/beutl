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
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Does.Contain("does_not_exist.png"));
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
        Guid id = Guid.NewGuid();
        var sources = new[] { (id, "NonExistentProperty", sourceUri) };

        // Act
        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

        // Assert: the per-property failure path tags the entry with the GUID and property,
        // which lets us tell it apart from the source-not-found branch.
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Does.Contain(id.ToString()));
            Assert.That(result.FailedResources[0], Does.Contain("NonExistentProperty"));
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
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Is.EqualTo("BrokenFont"));
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
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Is.EqualTo("UnknownFamily"));
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
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Is.EqualTo("FamilyWithMissingFile"));
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithMixedFailingAndWorkingFamilies_ReportsBothCountAndFailures()
    {
        // Arrange: one family resolves to a real file, the other throws from the finder.
        string goodFontPath = Path.Combine(_testDir, "good.ttf");
        await File.WriteAllBytesAsync(goodFontPath, [1, 2, 3]);
        var service = new ResourceRelocationService(name => name switch
        {
            "GoodFamily" => [goodFontPath],
            "BadFamily" => throw new InvalidOperationException("simulated"),
            _ => Array.Empty<string>(),
        });
        var fonts = new[] { new FontFamily("GoodFamily"), new FontFamily("BadFamily") };

        // Act
        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Is.EqualTo("BadFamily"));
            Assert.That(File.Exists(Path.Combine(_projectDir, "resources", "fonts", "good.ttf")), Is.True);
        });
    }

    [Test]
    public async Task RelocateFontsAsync_WithDuplicateFontFilesAcrossFamilies_CopiesOnce()
    {
        // Arrange: two families share the same physical font file.
        string sharedPath = Path.Combine(_testDir, "shared.ttf");
        await File.WriteAllBytesAsync(sharedPath, [1, 2, 3]);
        var service = new ResourceRelocationService(_ => new[] { sharedPath });
        var fonts = new[] { new FontFamily("Family1"), new FontFamily("Family2") };

        // Act
        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailedResources, Is.Empty);
            string[] copied = Directory.GetFiles(Path.Combine(_projectDir, "resources", "fonts"));
            Assert.That(copied, Has.Length.EqualTo(1));
        });
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithSameMissingUriReferencedTwice_ReportsSingleFailure()
    {
        // Arrange: two properties point at the same missing file. GroupBy on OriginalUri
        // means the failure is reported once, not twice.
        var service = new ResourceRelocationService();
        var project = new Project();
        Uri missingUri = new(Path.Combine(_testDir, "missing.png"));
        var sources = new[]
        {
            (Guid.NewGuid(), "Prop1", missingUri),
            (Guid.NewGuid(), "Prop2", missingUri),
        };

        // Act
        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
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
            Assert.That(result.FailedResources, Is.Empty);
        });
    }

    [Test]
    public void RelocateFileSourcesAsync_WithCancelledCopy_PropagatesCancellation()
    {
        // Arrange: a token already cancelled. CopyFileAsync should throw OCE
        // before we touch failedResources.
        var service = new ResourceRelocationService();
        var project = new Project();
        string sourceFilePath = Path.Combine(_testDir, "src.bin");
        File.WriteAllBytes(sourceFilePath, [1, 2, 3]);
        var sources = new[] { (Guid.NewGuid(), "Prop", new Uri(sourceFilePath)) };
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.RelocateFileSourcesAsync(sources, project, _projectDir, cts.Token));
    }

    [Test]
    public void RelocateFontsAsync_WithCancelledCopy_PropagatesCancellation()
    {
        // Arrange
        string fontPath = Path.Combine(_testDir, "font.ttf");
        File.WriteAllBytes(fontPath, [1, 2, 3]);
        var service = new ResourceRelocationService(_ => new[] { fontPath });
        var fonts = new[] { new FontFamily("AnyFamily") };
        using CancellationTokenSource cts = new();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.RelocateFontsAsync(fonts, _projectDir, cts.Token));
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
            Assert.That(result.FailedResources, Is.Empty);
        });
    }
}
