using System.Collections.ObjectModel;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class ResourceRelocationServiceTests
{
    private string _testDir = null!;
    private string _projectDir = null!;

    [SetUp]
    public void Setup()
    {
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
            }
        }
    }

    // Export finds a font where the editor loaded it, so a folder added in Settings waits for the restart too.
    [Test]
    public void FontSearchDirectories_AreTheFoldersTheFontManagerLoaded()
    {
        string projectFonts = Path.Combine(_projectDir, "resources", "fonts");
        IReadOnlyList<string> loaded = FontManager.Instance.FontDirectories;
        ObservableCollection<string> configured = GlobalConfiguration.Instance.FontConfig.FontDirectories;
        string added = Path.Combine(_testDir, "added-fonts");
        configured.Add(added);
        try
        {
            Assert.That(
                ResourceRelocationService.FontSearchDirectories(projectFonts),
                Is.EqualTo(new[] { projectFonts }.Concat(loaded)));
        }
        finally
        {
            configured.Remove(added);
        }
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithMissingSourceFile_ReportsFailedItem()
    {
        var service = new ResourceRelocationService();
        var project = new Project();
        string missingPath = Path.Combine(_testDir, "does_not_exist.png");
        Uri missingUri = new(missingPath);
        var sources = new[] { (Guid.NewGuid(), "Source", missingUri) };

        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

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
    public async Task RelocateFileSourcesAsync_StopsObjectLookupsAtMatchingId()
    {
        string sourcePath = Path.Combine(_testDir, "asset.bin");
        await File.WriteAllTextAsync(sourcePath, "media bytes");
        var source = new TestFileSource(new Uri(sourcePath));
        var owner = new CountingProjectItem { Source = source };
        var remaining = Enumerable.Range(0, 32).Select(_ => new CountingProjectItem()).ToArray();
        var project = new Project();
        project.Items.Replace([owner, .. remaining]);

        RelocationResult result = await new ResourceRelocationService().RelocateFileSourcesAsync(
            [(owner.Id.Value, nameof(CountingProjectItem.Source), source.Uri)], project, _projectDir);

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(source.Uri.LocalPath, Is.EqualTo(Path.Combine(_projectDir, "resources", "asset.bin")));
            Assert.That(File.ReadAllText(source.Uri.LocalPath), Is.EqualTo("media bytes"));
            Assert.That(remaining.Sum(item => item.Writes), Is.Zero);
        });
    }

    // The finder has no file for the family (the font is not installed), or a file that does not
    // exist, so CopyFileAsync throws.
    private static IEnumerable<TestCaseData> FontFindersWithoutACopyableFile()
    {
        yield return new TestCaseData((Func<string, IEnumerable<string>>)(_ => []))
            .SetArgDisplayNames("no files");
        string missingFontPath = Path.Combine(Path.GetTempPath(), $"beutl_missing_{Guid.NewGuid():N}.ttf");
        yield return new TestCaseData((Func<string, IEnumerable<string>>)(_ => [missingFontPath]))
            .SetArgDisplayNames("missing file");
    }

    [TestCaseSource(nameof(FontFindersWithoutACopyableFile))]
    public async Task RelocateFontsAsync_WithoutACopyableFontFile_ReportsFailedItem(
        Func<string, IEnumerable<string>> fontFileFinder)
    {
        var service = new ResourceRelocationService(fontFileFinder);
        var fonts = new[] { new FontFamily("UnknownFamily") };

        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
            Assert.That(result.FailedResources[0], Is.EqualTo("UnknownFamily"));
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

        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

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

        RelocationResult result = await service.RelocateFontsAsync(fonts, _projectDir);

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

        RelocationResult result = await service.RelocateFileSourcesAsync(sources, project, _projectDir);

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithNoSources_ReturnsEmptyResult()
    {
        var service = new ResourceRelocationService();
        var project = new Project();

        RelocationResult result = await service.RelocateFileSourcesAsync([], project, _projectDir);

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Is.Empty);
        });
    }

    [Test]
    public async Task RelocateFileSourcesAsync_WithCancelledCopy_PropagatesCancellation()
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

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.RelocateFileSourcesAsync(sources, project, _projectDir, cts.Token));
    }

    [Test]
    public async Task RelocateFontsAsync_WithCancelledCopy_PropagatesCancellation()
    {
        string fontPath = Path.Combine(_testDir, "font.ttf");
        File.WriteAllBytes(fontPath, [1, 2, 3]);
        var service = new ResourceRelocationService(_ => new[] { fontPath });
        var fonts = new[] { new FontFamily("AnyFamily") };
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.RelocateFontsAsync(fonts, _projectDir, cts.Token));
    }

    [Test]
    public async Task RelocateFontsAsync_WithNoFamilies_ReturnsEmptyResult()
    {
        var service = new ResourceRelocationService(_ => []);

        RelocationResult result = await service.RelocateFontsAsync([], _projectDir);

        Assert.Multiple(() =>
        {
            Assert.That(result.SuccessCount, Is.EqualTo(0));
            Assert.That(result.FailedResources, Is.Empty);
        });
    }

    // Fonts are searched only where FontManager loads them from, so a font left in an OS font folder that
    // is not configured is reported instead of bundled.
    [Test]
    [NonParallelizable]
    public async Task RelocateFontsAsync_WithAFontOnlyInAnUnconfiguredUserFontFolder_ReportsFailedItem()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("The Windows user font folder cannot be redirected through HOME.");

        string home = Path.Combine(_testDir, "home");
        string userFonts = Path.Combine(home, OperatingSystem.IsMacOS() ? "Library/Fonts" : ".fonts");
        Directory.CreateDirectory(userFonts);
        using (Stream source = typeof(ResourceRelocationServiceTests).Assembly
                   .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.BeutlTestVariable.ttf")!)
        using (Stream destination = File.Create(Path.Combine(userFonts, "custom.ttf")))
            source.CopyTo(destination);
        string? originalHome = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", home);
        try
        {
            RelocationResult result = await new ResourceRelocationService().RelocateFontsAsync(
                [new FontFamily("Beutl Test Variable")], _projectDir);

            Assert.Multiple(() =>
            {
                Assert.That(result.SuccessCount, Is.EqualTo(0));
                Assert.That(result.FailedResources, Is.EqualTo(new[] { "Beutl Test Variable" }));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
        }
    }

    private sealed class CountingProjectItem : ProjectItem
    {
        public TestFileSource? Source { get; init; }

        public int Writes { get; private set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            Writes++;
            base.Serialize(context);
            context.SetValue(nameof(Source), Source);
        }
    }
}
