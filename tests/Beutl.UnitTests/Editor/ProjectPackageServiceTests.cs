using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;
using NuGet.Versioning;

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
            }
        }
    }

    #region ExportAsync Tests

    [Test]
    public async Task ExportAsync_WithNullProject_ThrowsArgumentNullException()
    {
        var service = ProjectPackageService.Current;
        string outputPath = Path.Combine(_exportDir, "test.zip");

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ExportAsync(null!, outputPath));
    }

    [Test]
    public async Task ExportAsync_WithNullOutputPath_ThrowsArgumentNullException()
    {
        var service = ProjectPackageService.Current;
        var project = new Project();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ExportAsync(project, null!));
    }

    [Test]
    public async Task ExportAsync_WithUnsavedProject_ThrowsInvalidOperationException()
    {
        var service = ProjectPackageService.Current;
        var project = new Project(); // Uri is null
        string outputPath = Path.Combine(_exportDir, "test.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExportAsync(project, outputPath));
    }

    [Test]
    public async Task ExportAsync_WithValidProject_ExportsSuccessfully()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        ExportResult result = await service.ExportAsync(project, outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(File.Exists(outputPath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.ExportAsync(project, outputPath, cancellationToken: cts.Token));
    }

    [Test]
    public async Task ExportAsync_ExcludesBeutlDirectory()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        string beutlDir = Path.Combine(_projectDir, ".beutl");
        Directory.CreateDirectory(beutlDir);
        File.WriteAllText(Path.Combine(beutlDir, "state.json"), "{}");

        ExportResult result = await service.ExportAsync(project, outputPath);

        Assert.That(result.Success, Is.True);
        using var archive = ZipFile.OpenRead(outputPath);
        Assert.That(archive.Entries.Select(entry => entry.FullName), Has.None.StartsWith(".beutl/"));
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
        var stub = new StubRelocationService(
            new RelocationResult(0, ["missing/only_file.png"]),
            new RelocationResult(0, []));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "file_only.zip");

        ExportResult result = await service.ExportAsync(project, outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "missing/only_file.png" }));
        });
    }

    [Test]
    public async Task ExportAsync_WithOnlyFontFailures_SurfacesFontFailures()
    {
        var stub = new StubRelocationService(
            new RelocationResult(0, []),
            new RelocationResult(0, ["MissingFontFamily"]));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "font_only.zip");

        ExportResult result = await service.ExportAsync(project, outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "MissingFontFamily" }));
        });
    }

    [Test]
    public async Task ExportAsync_WhenZipCreationFails_PreservesAlreadyCollectedFailures()
    {
        // Failures collected before the ZIP step fails must still be reported.
        var stub = new StubRelocationService(
            new RelocationResult(0, ["pre_abort_file.png"]),
            new RelocationResult(0, ["pre_abort_font"]));
        var service = new ProjectPackageService(stub);
        Project project = CreateAndSaveTestProject();
        string invalidOutputPath = Path.Combine(_exportDir, "invalid_output_dir");
        Directory.CreateDirectory(invalidOutputPath);

        ExportResult result = await service.ExportAsync(project, invalidOutputPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailedResources, Is.EqualTo(new[] { "pre_abort_file.png", "pre_abort_font" }));
        });
    }

    #endregion

    #region ImportAsync Tests

    [Test]
    public async Task ImportAsync_WithNullPackagePath_ThrowsArgumentNullException()
    {
        var service = ProjectPackageService.Current;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ImportAsync(null!, _importDir));
    }

    [Test]
    public async Task ImportAsync_WithNullDestinationDirectory_ThrowsArgumentNullException()
    {
        var service = ProjectPackageService.Current;
        string packagePath = Path.Combine(_exportDir, "test.zip");

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ImportAsync(packagePath, null!));
    }

    [Test]
    public async Task ImportAsync_WithNonExistentPackage_ThrowsFileNotFoundException()
    {
        var service = ProjectPackageService.Current;
        string packagePath = Path.Combine(_exportDir, "nonexistent.zip");

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await service.ImportAsync(packagePath, _importDir));
    }

    [Test]
    public async Task ImportAsync_WithValidPackage_ImportsSuccessfully()
    {
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(importedProject, Is.Not.Null);
    }

    [Test]
    public async Task ImportAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        var ex = await Assert.CatchAsync<Exception>(async () =>
            await service.ImportAsync(packagePath, _importDir, cancellationToken: cts.Token));
        Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
    }

    [TestCase(1, "test_1")]
    [TestCase(3, "test_3")]
    public async Task ImportAsync_WhenDestinationExists_CreatesUniqueDirectory(int existingDirectories, string expected)
    {
        var service = ProjectPackageService.Current;
        Project originalProject = CreateAndSaveTestProject();
        string packagePath = Path.Combine(_exportDir, "test.zip");
        await service.ExportAsync(originalProject, packagePath);

        for (int i = 0; i < existingDirectories; i++)
            Directory.CreateDirectory(Path.Combine(_importDir, i == 0 ? "test" : $"test_{i}"));

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(importedProject, Is.Not.Null);
        Assert.That(Path.GetFileName(Path.GetDirectoryName(importedProject!.Uri!.LocalPath)), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImportAsync_WithProjectNeedingNewerVersion_ThrowsAndImportsNothing(bool elementNeedsIt)
    {
        // Arrange: the project file, or an element file its scene reads in parallel, needs a newer Beutl
        string packageDir = Path.Combine(_testDir, "newer");
        var project = new Project { Uri = new Uri(Path.Combine(packageDir, "newer.bep")) };
        var scene = new Scene { Uri = new Uri(Path.Combine(packageDir, "scene", "scene.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(packageDir, "scene", "element.belm")) };
        scene.AddChild(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri);
        string newerFile = elementNeedsIt ? element.Uri.LocalPath : project.Uri.LocalPath;
        string required = $"{NuGetVersion.Parse(BeutlApplication.Version).Major + 1}.0.0";
        JsonObject json = JsonNode.Parse(File.ReadAllText(newerFile))!.AsObject();
        json["minAppVersion"] = required;
        json.JsonSave(newerFile);
        string packagePath = Path.Combine(_exportDir, "newer.zip");
        ZipFile.CreateFromDirectory(packageDir, packagePath);

        UnsupportedFileVersionException? refused = await Assert.ThrowsAsync<UnsupportedFileVersionException>(
            async () => await ProjectPackageService.Current.ImportAsync(packagePath, _importDir));

        Assert.Multiple(() =>
        {
            Assert.That(refused!.RequiredVersion, Is.EqualTo(required));
            Assert.That(Path.GetFileName(refused.Uri.LocalPath), Is.EqualTo(Path.GetFileName(newerFile)));
            Assert.That(Directory.EnumerateFileSystemEntries(_importDir), Is.Empty);
        });
    }

    #endregion

    #region CopyDirectoryAsync Edge Cases

    [Test]
    public async Task ExportAsync_WithEmptySubDirectories_ExportsSuccessfully()
    {
        var service = ProjectPackageService.Current;
        Project project = CreateAndSaveTestProject();
        string outputPath = Path.Combine(_exportDir, "test.zip");

        Directory.CreateDirectory(Path.Combine(_projectDir, "empty_dir"));

        ExportResult result = await service.ExportAsync(project, outputPath);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ImportAsync_WithPackageWithoutProjectFile_CleansUpExtractedDirectory()
    {
        var service = ProjectPackageService.Current;

        string tempDir = Path.Combine(_testDir, "noprojectcleanup");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "dummy.txt"), "content");
        string packagePath = Path.Combine(_exportDir, "noprojectcleanup.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, packagePath);

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        Assert.That(importedProject, Is.Null);
        Assert.That(Directory.GetDirectories(_importDir, ".beutl-import-*"), Is.Empty);
    }

    [Test]
    public async Task ImportAsync_WithInvalidProjectFile_CleansUpExtractedDirectory()
    {
        var service = ProjectPackageService.Current;

        string tempDir = Path.Combine(_testDir, "invalidbep");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.bep"), "this is not valid bep content");
        string packagePath = Path.Combine(_exportDir, "invalidbep.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, packagePath);

        Project? importedProject = await service.ImportAsync(packagePath, _importDir);

        // Assert - should return null because RestoreFromUri fails
        Assert.That(importedProject, Is.Null);
        Assert.That(Directory.GetDirectories(_importDir, ".beutl-import-*"), Is.Empty);
    }

    #endregion

    #region Error Handling

    [Test]
    public async Task ImportAsync_WhenPackageIsCorrupt_ReturnsNull()
    {
        var service = ProjectPackageService.Current;

        string corruptPackagePath = Path.Combine(_exportDir, "corrupt.zip");
        File.WriteAllText(corruptPackagePath, "This is not a valid ZIP file");

        Project? result = await service.ImportAsync(corruptPackagePath, _importDir);

        // Assert - should return null because the import failed
        Assert.That(result, Is.Null);
    }

    #endregion

    [Test]
    public async Task ExportAsync_CancelledBeforeZipCreation_PreservesTheExistingPackage()
    {
        Project project = CreateAndSaveTestProject();
        string output = Path.Combine(_exportDir, "previous.beutlpkg");
        File.WriteAllText(output, "previous complete package");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value.Progress == 0.9) cancellation.Cancel();
        });

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await ProjectPackageService.Current.ExportAsync(project, output, progress, cancellation.Token));

        Assert.That(File.ReadAllText(output), Is.EqualTo("previous complete package"));
        Assert.That(Directory.GetDirectories(_exportDir), Is.Empty);
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExportAsync_ExcludesGitDirectoriesAndWorktreePointers(bool worktree)
    {
        Project project = CreateAndSaveTestProject();
        string git = Path.Combine(_projectDir, ".git");
        if (worktree)
            File.WriteAllText(git, "gitdir: /private/original/repository/.git/worktrees/project");
        else
        {
            Directory.CreateDirectory(Path.Combine(git, "objects"));
            File.WriteAllText(Path.Combine(git, "objects", "removed-content"), "private deleted content");
        }
        string nested = Directory.CreateDirectory(Path.Combine(_projectDir, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: /private/submodule");
        File.WriteAllText(Path.Combine(nested, "asset.txt"), "shared asset");
        File.WriteAllText(Path.Combine(_projectDir, ".gitignore"), ".beutl/");
        string output = Path.Combine(_exportDir, "shared.beutlpkg");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, output);

        Assert.That(result.Success, Is.True);
        using var archive = ZipFile.OpenRead(output);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.That(entries.Any(path => path.Split('/').Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))), Is.False);
        Assert.That(entries, Does.Contain(".gitignore").And.Contain("nested/asset.txt"));
        Assert.That(worktree ? File.Exists(git) : Directory.Exists(git), Is.True, "Export must not alter the source repository.");
    }

    [Test]
    [TestCase(".git", false)]
    [TestCase(".git", true)]
    [TestCase(".GIT", false)]
    [TestCase(".GIT", true)]
    public async Task ImportAsync_RemovesGitMetadataBeforePublishing(string gitName, bool worktree)
    {
        CreateAndSaveTestProject();
        string git = Path.Combine(_projectDir, gitName);
        if (worktree)
            File.WriteAllText(git, "gitdir: /untrusted/repository");
        else
        {
            Directory.CreateDirectory(git);
            File.WriteAllText(Path.Combine(git, "config"), "[core]\nfsmonitor = untrusted-command\n");
        }
        string nested = Directory.CreateDirectory(Path.Combine(_projectDir, "assets")).FullName;
        File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: /untrusted/submodule");
        File.WriteAllText(Path.Combine(nested, "keep.txt"), "shared asset");
        File.WriteAllText(Path.Combine(_projectDir, ".gitignore"), "*.tmp");
        File.WriteAllText(Path.Combine(_projectDir, ".gitattributes"), "*.scene text");
        string package = Path.Combine(_exportDir, "legacy.beutlpkg");
        ZipFile.CreateFromDirectory(_projectDir, package);

        Project? imported = await ProjectPackageService.Current.ImportAsync(package, _importDir);

        Assert.That(imported, Is.Not.Null);
        string root = Path.GetDirectoryName(imported!.Uri!.LocalPath)!;
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(root, "assets", "keep.txt")), Is.EqualTo("shared asset"));
            Assert.That(File.Exists(Path.Combine(root, ".gitignore")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, ".gitattributes")), Is.True);
            Assert.That(worktree ? File.Exists(git) : Directory.Exists(git), Is.True);
            Assert.That(Directory.GetDirectories(_importDir, ".beutl-import-*"), Is.Empty);
        });
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ImportAsync_ConcurrentImportsPreserveBothSuccessfulDestinations(bool secondFails)
    {
        CreateAndSaveTestProject();
        string package = Path.Combine(_exportDir, "shared.beutlpkg");
        ZipFile.CreateFromDirectory(_projectDir, package);
        string secondPackage = package;
        if (secondFails)
        {
            string other = Directory.CreateDirectory(Path.Combine(_exportDir, "other")).FullName;
            secondPackage = Path.Combine(other, "shared.beutlpkg");
            using var archive = ZipFile.Open(secondPackage, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("test.bep").Open());
            writer.Write("invalid project");
        }

        using var arrived = new CountdownEvent(2);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        InlineProgress Gate(ManualResetEventSlim release) => new(value =>
        {
            if (value.Progress != 0.3) return;
            arrived.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException();
        });
        Task<Project?> firstTask = Task.Run(() => ProjectPackageService.Current.ImportAsync(package, _importDir, Gate(releaseFirst)));
        Task<Project?> secondTask = Task.Run(() => ProjectPackageService.Current.ImportAsync(secondPackage, _importDir, Gate(releaseSecond)));
        try
        {
            Assert.That(arrived.Wait(TimeSpan.FromSeconds(10)), Is.True);
            releaseFirst.Set();
            Project? first = await firstTask;
            Assert.That(first, Is.Not.Null);
            string firstFile = first!.Uri!.LocalPath;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(firstFile)!, "keep.txt"), "edits after first import");

            releaseSecond.Set();
            Project? second = await secondTask;

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(firstFile), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(firstFile)!, "keep.txt")), Is.EqualTo("edits after first import"));
                Assert.That(second is null, Is.EqualTo(secondFails));
                if (!secondFails)
                {
                    Assert.That(second!.Uri, Is.Not.EqualTo(first.Uri));
                    Assert.That(File.Exists(second.Uri!.LocalPath), Is.True);
                }
                Assert.That(Directory.GetDirectories(_importDir, ".beutl-import-*"), Is.Empty);
            });
        }
        finally
        {
            releaseFirst.Set();
            releaseSecond.Set();
            await Task.WhenAll(firstTask, secondTask);
        }
    }

    [Test]
    public async Task ImportAsync_RelativeSceneUrisUseThePublishedDirectory()
    {
        Project original = CreateAndSaveTestProjectWithItems();
        string package = Path.Combine(_exportDir, "scenes.beutlpkg");
        ZipFile.CreateFromDirectory(_projectDir, package);

        Project? imported = await ProjectPackageService.Current.ImportAsync(package, _importDir);

        Assert.That(imported, Is.Not.Null);
        string root = Path.GetDirectoryName(imported!.Uri!.LocalPath)!;
        Assert.That(imported.Items, Has.Count.EqualTo(original.Items.Count));
        Assert.That(imported.Items[0].Uri!.LocalPath, Is.EqualTo(Path.Combine(root, "test_scene.scene")));
        Assert.That(File.Exists(imported.Items[0].Uri!.LocalPath), Is.True);
        Assert.That(Directory.GetDirectories(_importDir, ".beutl-import-*"), Is.Empty);
    }

    [Test]
    public async Task ImportAsync_CancelledAfterExtractionRemovesOnlyItsStaging()
    {
        CreateAndSaveTestProject();
        string package = Path.Combine(_exportDir, "cancelled.beutlpkg");
        ZipFile.CreateFromDirectory(_projectDir, package);
        string existing = Directory.CreateDirectory(Path.Combine(_importDir, "cancelled")).FullName;
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "existing data");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value.Progress == 0.8) cancellation.Cancel();
        });

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await ProjectPackageService.Current.ImportAsync(package, _importDir, progress, cancellation.Token));
        Assert.That(File.ReadAllText(Path.Combine(existing, "keep.txt")), Is.EqualTo("existing data"));
        Assert.That(Directory.GetDirectories(_importDir), Is.EqualTo(new[] { existing }));
    }

    [Test]
    public async Task ExportAsync_PreservesExistingPackagePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required.");
            return;
        }

        Project project = CreateAndSaveTestProject();
        string package = Path.Combine(_exportDir, "private.beutlpkg");
        File.WriteAllText(package, "previous package");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(package, mode);

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, package);

        Assert.That(result.Success, Is.True);
        Assert.That(File.GetUnixFileMode(package), Is.EqualTo(mode));
        using var archive = ZipFile.OpenRead(package);
        Assert.That(archive.GetEntry("test.bep"), Is.Not.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExportAsync_KeepsStagedPrivateFilesInAnOwnerOnlyDirectory(bool cancel)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file permissions are required.");
            return;
        }

        Project project = CreateAndSaveTestProject();
        string fileName = $"private-{Guid.NewGuid():N}.txt";
        string source = Path.Combine(_projectDir, fileName);
        File.WriteAllText(source, "private client data");
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string? staging = null;
        UnixFileMode? stagingMode = null;
        string? copiedContents = null;
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value.Progress == 0.2 && !OperatingSystem.IsWindows())
            {
                staging = Directory.EnumerateDirectories(Path.GetTempPath(), "beutl_export_*")
                    .Single(directory => File.Exists(Path.Combine(directory, Path.GetFileName(_projectDir), fileName)));
                stagingMode = File.GetUnixFileMode(staging);
                copiedContents = File.ReadAllText(Path.Combine(staging, Path.GetFileName(_projectDir), fileName));
            }
            if (cancel && value.Progress == 0.9)
                cancellation.Cancel();
        });
        string output = Path.Combine(_exportDir, "private.beutl");
        File.WriteAllText(output, "previous package");

        if (cancel)
        {
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await ProjectPackageService.Current.ExportAsync(project, output, progress, cancellation.Token));
            Assert.That(File.ReadAllText(output), Is.EqualTo("previous package"));
        }
        else
        {
            ExportResult result = await ProjectPackageService.Current.ExportAsync(project, output, progress);
            Assert.That(result.Success, Is.True);
        }

        UnixFileMode finalSourceMode = File.GetUnixFileMode(source);
        Assert.Multiple(() =>
        {
            Assert.That(staging, Is.Not.Null);
            Assert.That(stagingMode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(copiedContents, Is.EqualTo("private client data"));
            Assert.That(Directory.Exists(staging), Is.False);
            Assert.That(finalSourceMode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
    }

    [TestCase("shared.beutl")]
    [TestCase("exports/shared.beutl")]
    public async Task ExportAsync_ReplacingAPackageInTheProjectDoesNotEmbedItsPreviousContents(string relativeOutput)
    {
        Project project = CreateAndSaveTestProject();
        string source = Path.Combine(_projectDir, "removed-private.txt");
        File.WriteAllText(source, "private data from an earlier export");
        string output = Path.Combine(_projectDir, relativeOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Assert.That((await ProjectPackageService.Current.ExportAsync(project, output)).Success, Is.True);
        using (var previous = ZipFile.OpenRead(output))
            Assert.That(previous.GetEntry("removed-private.txt"), Is.Not.Null);
        File.Delete(source);

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, output);

        Assert.That(result.Success, Is.True);
        Assert.That(result.FailedResources, Is.Empty);
        using var archive = ZipFile.OpenRead(output);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry("test.bep"), Is.Not.Null);
            Assert.That(archive.GetEntry("removed-private.txt"), Is.Null);
            Assert.That(archive.GetEntry(relativeOutput), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_ExcludesAnOutputReachedThroughADirectoryAlias()
    {
        Project project = CreateAndSaveTestProject();
        string output = Path.Combine(_projectDir, "shared.beutl");
        File.WriteAllText(output, "previous private package");
        string alias = Path.Combine(_exportDir, "alias");
        SymbolicLinks.CreateOrIgnore(alias, _projectDir, directory: true);

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, Path.Combine(alias, "shared.beutl"));

        Assert.That(result.Success, Is.True);
        using var archive = ZipFile.OpenRead(output);
        Assert.That(archive.GetEntry("shared.beutl"), Is.Null);
        Assert.That(archive.GetEntry("test.bep"), Is.Not.Null);
    }

    [Test]
    public async Task ExportAsync_FailedSidecarRelocationPreservesTheOriginalAndPreviousPackage()
    {
        Project project = CreateAndSaveTestProject();
        string external = Directory.CreateDirectory(Path.Combine(_testDir, "external-scene")).FullName;
        var scene = new Scene(64, 64, "External") { Uri = new Uri(Path.Combine(external, "external.scene")) };
        var element = new Element { Uri = new Uri(Path.Combine(external, "clip.belm")) };
        scene.Children.Add(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri!);
        byte[] originalScene = File.ReadAllBytes(scene.Uri.LocalPath);
        byte[] originalElement = File.ReadAllBytes(element.Uri.LocalPath);
        string output = Path.Combine(_exportDir, "previous.beutl");
        File.WriteAllText(output, "previous complete package");
        var service = new ProjectPackageService(new FailedSceneRelocationService());

        ExportResult result = await service.ExportAsync(project, output);

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailedResources, Is.Not.Empty);
        Assert.That(File.ReadAllBytes(scene.Uri.LocalPath), Is.EqualTo(originalScene));
        Assert.That(File.ReadAllBytes(element.Uri.LocalPath), Is.EqualTo(originalElement));
        Assert.That(File.ReadAllText(output), Is.EqualTo("previous complete package"));
    }

    private sealed class FailedSceneRelocationService : ResourceRelocationService
    {
        public override Task<RelocationResult> RelocateFileSourcesAsync(
            IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> sources, Project stagingProject,
            string projectDirectory, CancellationToken cancellationToken = default)
        {
            Scene scene = stagingProject.Items.OfType<Scene>().Single(item => item.Name.Value == "External");
            Element element = scene.Children.Single();
            string destination = Path.Combine(projectDirectory, "clip.belm");
            File.Copy(element.Uri!.LocalPath, destination);
            element.Uri = new Uri(destination);
            return Task.FromResult(new RelocationResult(1, [scene.Uri!.LocalPath]));
        }
    }

    [Test]
    public async Task ExportAsync_ImportAsync_PreservesAssetInCaseVariantDirectory()
    {
        Project project = CreateAndSaveTestProjectWithItems();
        string assetDir = Path.Combine(_testDir, "Project");
        Directory.CreateDirectory(assetDir);
        string asset = Path.Combine(assetDir, "asset.png");
        ImageSource source = TestImages.WriteImage(asset);
        byte[] imageBytes = File.ReadAllBytes(asset);
        var image = new SourceImage();
        image.Source.Value = source;
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.Objects.Add(image);
        CoreSerializer.StoreToUri(element, new Uri(Path.Combine(_projectDir, "image.belm")));
        Scene scene = project.Items.OfType<Scene>().Single();
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, scene.Uri!);
        CoreSerializer.StoreToUri(project, project.Uri!);
        project = CoreSerializer.RestoreFromUri<Project>(project.Uri!);
        string package = Path.Combine(_exportDir, "assets.beutlpkg");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, package);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FailedResources, Is.Empty);
        File.Delete(asset);

        Project? imported = await ProjectPackageService.Current.ImportAsync(package, _importDir);

        Assert.That(imported, Is.Not.Null);
        ImageSource importedSource = imported!.Items.OfType<Scene>().Single().Children.Single()
            .Objects.OfType<SourceImage>().Single().Source.Value!;
        string importedRoot = Path.GetDirectoryName(imported.Uri!.LocalPath)!;
        Assert.That(importedSource.Uri.LocalPath, Does.StartWith(importedRoot + Path.DirectorySeparatorChar));
        Assert.That(File.ReadAllBytes(importedSource.Uri.LocalPath), Is.EqualTo(imageBytes));
    }

    #region Helper Methods

    [TestCase(false)]
    [TestCase(true)]
    public async Task Exported_fingerprints_follow_packaged_media_and_match_it_after_a_rename(bool external)
    {
        Project project = CreateAndSaveTestProjectWithItems();
        Scene scene = project.Items.OfType<Scene>().Single();
        string mediaDirectory = Path.Combine(external ? _testDir : _projectDir, "media");
        Directory.CreateDirectory(mediaDirectory);
        string original = Path.Combine(mediaDirectory, "original.png");
        ImageSource source = TestImages.WriteImage(original);
        var element = new Element { Uri = new Uri(Path.Combine(_projectDir, "media.belm")) };
        element.Objects.Add(new SourceImage { Source = { Value = source } });
        scene.Children.Add(element);
        var media = new MissingMediaService();
        await media.UpdateFingerprintsAsync(scene);
        string expectedHash = scene.MediaFingerprints[new Uri(original).AbsoluteUri].Sha256;
        CoreSerializer.StoreToUri(project, project.Uri!);
        string fingerprintFile = MediaFingerprintFile.GetPath(scene.Uri!.LocalPath);
        byte[] originalFingerprints = File.ReadAllBytes(fingerprintFile);
        string package = Path.Combine(_exportDir, "fingerprints.zip");
        Assert.That((await ProjectPackageService.Current.ExportAsync(project, package)).Success, Is.True);
        // The export saves its copy of the scene with the packaged media's fingerprints, into the copy's folder.
        Assert.That(File.ReadAllBytes(fingerprintFile), Is.EqualTo(originalFingerprints));
        ZipFile.ExtractToDirectory(package, _importDir);
        var packaged = CoreSerializer.RestoreFromUri<Project>(new Uri(Path.Combine(_importDir, Path.GetFileName(project.Uri!.LocalPath))));
        Scene packagedScene = packaged.Items.OfType<Scene>().Single();
        var packagedSource = packagedScene.Children.Single().Objects.OfType<SourceImage>().Single().Source.Value!;
        Assert.That(packagedScene.MediaFingerprints, Has.Count.EqualTo(1));
        Assert.That(packagedScene.MediaFingerprints.ContainsKey(new Uri(original).AbsoluteUri), Is.False);
        Assert.That(packagedScene.MediaFingerprints[packagedSource.Uri.AbsoluteUri].Sha256, Is.EqualTo(expectedHash));
        string renamed = Path.Combine(Path.GetDirectoryName(packagedSource.Uri.LocalPath)!, "renamed.png");
        File.Move(packagedSource.Uri.LocalPath, renamed);
        var missing = media.FindMissing(packagedScene).Single();
        Assert.That((await media.FindMatchesAsync([missing], _importDir))[missing].Single(), Is.EqualTo(renamed));
        Assert.That(scene.MediaFingerprints[new Uri(original).AbsoluteUri].Sha256, Is.EqualTo(expectedHash), "Export must leave the original graph intact.");
    }

    private sealed class InlineProgress(Action<(string Message, double Progress)> report)
        : IProgress<(string Message, double Progress)>
    {
        public void Report((string Message, double Progress) value) => report(value);
    }

    private Project CreateAndSaveTestProject()
    {
        string projectFilePath = Path.Combine(_projectDir, "test.bep");
        var project = new Project();
        Uri projectUri = new(projectFilePath);
        project.Name.Value = "TestProject";

        CoreSerializer.StoreToUri(project, projectUri);

        // Restore the project to get a proper Uri set
        return CoreSerializer.RestoreFromUri<Project>(projectUri);
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
        project.Name.Value = "TestProjectWithItems";

        var scene = new Scene(1920, 1080, "TestScene");
        string sceneFilePath = Path.Combine(_projectDir, "test_scene.scene");
        Uri sceneUri = new(sceneFilePath);
        CoreSerializer.StoreToUri(scene, sceneUri);

        // Restore the scene to get a proper Uri set
        scene = CoreSerializer.RestoreFromUri<Scene>(sceneUri);

        project.Items.Add(scene);

        CoreSerializer.StoreToUri(project, projectUri);

        // Restore the project to get a proper Uri set
        return CoreSerializer.RestoreFromUri<Project>(projectUri);
    }

    #endregion
}
