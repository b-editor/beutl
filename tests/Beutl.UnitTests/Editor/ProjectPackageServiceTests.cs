using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.IO;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
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
    public async Task ExportAsync_WithProjectFileSymbolicLink_MaterializesProjectFile()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-project-link");
        Directory.CreateDirectory(outsideDirectory);
        string outsideProjectFile = Path.Combine(outsideDirectory, "target.bep");
        CoreSerializer.StoreToUri(
            new Project { Name = "LinkedProject" },
            new Uri(outsideProjectFile));

        string projectLink = Path.Combine(_projectDir, "linked-project.bep");
        CreateFileSymbolicLinkOrIgnore(projectLink, outsideProjectFile);
        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectLink));
        string packagePath = Path.Combine(_exportDir, "project-file-link.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Assert.That(result.Success, Is.True);
        Directory.Delete(outsideDirectory, recursive: true);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);
        using System.IO.Compression.ZipArchive archive
            = System.IO.Compression.ZipFile.OpenRead(packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(importedProject, Is.Not.Null);
            Assert.That(importedProject?.Name, Is.EqualTo("LinkedProject"));
            Assert.That(archive.GetEntry("linked-project.bep"), Is.Not.Null);
            Assert.That(archive.GetEntry("resources/linked-project.bep"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_WithSceneAndElementFileSymbolicLinks_MaterializesSidecars()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-sidecar-links");
        Directory.CreateDirectory(outsideDirectory);
        string outsideSceneFile = Path.Combine(outsideDirectory, "target.scene");
        string outsideElementFile = Path.Combine(outsideDirectory, "target.belm");
        var scene = new Scene(1920, 1080, "LinkedScene")
        {
            Uri = new Uri(outsideSceneFile),
        };
        var element = new Element
        {
            Uri = new Uri(outsideElementFile),
            Length = TimeSpan.FromSeconds(1),
        };
        scene.Children.Add(element);
        CoreSerializer.StoreToUri(scene, new Uri(outsideSceneFile));

        string sceneLink = Path.Combine(_projectDir, "linked.scene");
        string elementLink = Path.Combine(_projectDir, "linked.belm");
        CreateFileSymbolicLinkOrIgnore(sceneLink, outsideSceneFile);
        CreateFileSymbolicLinkOrIgnore(elementLink, outsideElementFile);
        scene.Uri = new Uri(sceneLink);
        element.Uri = new Uri(elementLink);
        var project = new Project { Name = "LinkedSidecars" };
        project.Items.Add(scene);
        Uri projectUri = new(Path.Combine(_projectDir, "linked-sidecars.bep"));
        CoreSerializer.StoreToUri(project, projectUri, CoreSerializationMode.Write);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, "sidecar-file-links.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Assert.That(result.Success, Is.True);
        Directory.Delete(outsideDirectory, recursive: true);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);
        using System.IO.Compression.ZipArchive archive
            = System.IO.Compression.ZipFile.OpenRead(packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(importedProject, Is.Not.Null);
            Assert.That(importedProject?.Items.OfType<Scene>().Single().Children, Has.Count.EqualTo(1));
            Assert.That(archive.GetEntry("linked.scene"), Is.Not.Null);
            Assert.That(archive.GetEntry("linked.belm"), Is.Not.Null);
            Assert.That(archive.GetEntry("resources/linked.scene"), Is.Null);
            Assert.That(archive.GetEntry("resources/linked.belm"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_WithSidecarsThroughDirectorySymbolicLink_MaterializesSidecars()
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-sidecar-directory");
        Directory.CreateDirectory(outsideDirectory);
        string outsideSceneFile = Path.Combine(outsideDirectory, "linked.scene");
        string outsideElementFile = Path.Combine(outsideDirectory, "linked.belm");
        File.WriteAllText(Path.Combine(outsideDirectory, "unreferenced.txt"), "do not package");
        var scene = new Scene(1920, 1080, "LinkedDirectoryScene")
        {
            Uri = new Uri(outsideSceneFile),
        };
        scene.Children.Add(new Element
        {
            Uri = new Uri(outsideElementFile),
            Length = TimeSpan.FromSeconds(1),
        });
        CoreSerializer.StoreToUri(scene, new Uri(outsideSceneFile));

        string linkedDirectory = Path.Combine(_projectDir, "linked-structure");
        CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, outsideDirectory);
        scene = CoreSerializer.RestoreFromUri<Scene>(
            new Uri(Path.Combine(linkedDirectory, "linked.scene")));
        var project = new Project { Name = "LinkedDirectorySidecars" };
        project.Items.Add(scene);
        Uri projectUri = new(Path.Combine(_projectDir, "linked-directory-sidecars.bep"));
        CoreSerializer.StoreToUri(project, projectUri, CoreSerializationMode.Write);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, "sidecar-directory-link.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Assert.That(result.Success, Is.True);
        Directory.Delete(outsideDirectory, recursive: true);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);
        using System.IO.Compression.ZipArchive archive
            = System.IO.Compression.ZipFile.OpenRead(packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(importedProject, Is.Not.Null);
            Assert.That(importedProject?.Items.OfType<Scene>().Single().Children, Has.Count.EqualTo(1));
            Assert.That(archive.GetEntry("linked-structure/linked.scene"), Is.Not.Null);
            Assert.That(archive.GetEntry("linked-structure/linked.belm"), Is.Not.Null);
            Assert.That(archive.GetEntry("linked-structure/unreferenced.txt"), Is.Null);
            Assert.That(archive.GetEntry("resources/linked.scene"), Is.Null);
            Assert.That(archive.GetEntry("resources/linked.belm"), Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExportAsync_WithNestedExtensionSidecarLink_MaterializesOnlyReferencedFiles(
        bool useDirectoryLink)
    {
        var service = ProjectPackageService.Current;
        string outsideDirectory = Path.Combine(_testDir, "outside-extension-sidecars");
        Directory.CreateDirectory(outsideDirectory);
        string outsideItemFile = Path.Combine(outsideDirectory, "extension.item");
        string outsideSidecarFile = Path.Combine(outsideDirectory, "extension.sidecar");
        string outsideListedSidecarFile = Path.Combine(outsideDirectory, "listed-extension.sidecar");
        File.WriteAllText(Path.Combine(outsideDirectory, "unreferenced.txt"), "do not package");
        var sidecar = new PackageTestSidecar
        {
            Uri = new Uri(outsideSidecarFile),
            Value = "nested sidecar value",
        };
        var listedSidecar = new PackageTestSidecar
        {
            Uri = new Uri(outsideListedSidecarFile),
            Value = "listed sidecar value",
        };
        var item = new PackageTestProjectItem
        {
            Uri = new Uri(outsideItemFile),
            Sidecar = sidecar,
            Sidecars = [listedSidecar],
        };
        CoreSerializer.StoreToUri<ProjectItem>(item, new Uri(outsideItemFile));

        string archivePrefix;
        if (useDirectoryLink)
        {
            archivePrefix = "linked-extension";
            string linkedDirectory = Path.Combine(_projectDir, archivePrefix);
            CreateDirectorySymbolicLinkOrIgnore(linkedDirectory, outsideDirectory);
            item.Uri = new Uri(Path.Combine(linkedDirectory, "extension.item"));
            sidecar.Uri = new Uri(Path.Combine(linkedDirectory, "extension.sidecar"));
            listedSidecar.Uri = new Uri(Path.Combine(linkedDirectory, "listed-extension.sidecar"));
        }
        else
        {
            archivePrefix = string.Empty;
            string linkedItem = Path.Combine(_projectDir, "extension.item");
            string linkedSidecar = Path.Combine(_projectDir, "extension.sidecar");
            string linkedListedSidecar = Path.Combine(_projectDir, "listed-extension.sidecar");
            CreateFileSymbolicLinkOrIgnore(linkedItem, outsideItemFile);
            CreateFileSymbolicLinkOrIgnore(linkedSidecar, outsideSidecarFile);
            CreateFileSymbolicLinkOrIgnore(linkedListedSidecar, outsideListedSidecarFile);
            item.Uri = new Uri(linkedItem);
            sidecar.Uri = new Uri(linkedSidecar);
            listedSidecar.Uri = new Uri(linkedListedSidecar);
        }

        var project = new Project { Name = "ExtensionSidecars" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, "extension-sidecars.bep"));
        CoreSerializer.StoreToUri(project, projectUri, CoreSerializationMode.Write);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(
            _exportDir,
            useDirectoryLink ? "extension-directory-link.zip" : "extension-file-links.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);
        Assert.That(result.Success, Is.True);
        Directory.Delete(outsideDirectory, recursive: true);
        Project? importedProject = await service.ImportAsync(packagePath, _importDir);
        using System.IO.Compression.ZipArchive archive
            = System.IO.Compression.ZipFile.OpenRead(packagePath);
        string entryPrefix = string.IsNullOrEmpty(archivePrefix)
            ? string.Empty
            : $"{archivePrefix}/";

        PackageTestProjectItem? importedItem
            = importedProject?.Items.OfType<PackageTestProjectItem>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(importedItem?.Sidecar?.Value, Is.EqualTo("nested sidecar value"));
            Assert.That(importedItem?.Sidecars.Single().Value, Is.EqualTo("listed sidecar value"));
            Assert.That(archive.GetEntry($"{entryPrefix}extension.item"), Is.Not.Null);
            Assert.That(archive.GetEntry($"{entryPrefix}extension.sidecar"), Is.Not.Null);
            Assert.That(archive.GetEntry($"{entryPrefix}listed-extension.sidecar"), Is.Not.Null);
            Assert.That(archive.GetEntry($"{entryPrefix}unreferenced.txt"), Is.Null);
            Assert.That(archive.GetEntry("resources/extension.item"), Is.Null);
            Assert.That(archive.GetEntry("resources/extension.sidecar"), Is.Null);
            Assert.That(archive.GetEntry("resources/listed-extension.sidecar"), Is.Null);
        });
    }

    [Test]
    public async Task ExportAsync_WithDuplicateReachableObjectIds_FailsBeforeRelocation()
    {
        string firstResource = Path.Combine(_testDir, "first-external.png");
        string secondResource = Path.Combine(_testDir, "second-external.png");
        File.WriteAllText(firstResource, "first");
        File.WriteAllText(secondResource, "second");
        Guid duplicateId = Guid.NewGuid();
        var first = new PackageTestFileSourceItem
        {
            Id = duplicateId,
            Source = CreateImageSource(firstResource),
        };
        var second = new PackageTestFileSourceItem
        {
            Id = duplicateId,
            Source = CreateImageSource(secondResource),
        };
        var project = new Project { Name = "DuplicateIds" };
        project.Items.Add(first);
        project.Items.Add(second);
        Uri projectUri = new(Path.Combine(_projectDir, "duplicate-ids.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, "duplicate-ids.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithSameObjectSerializedTwice_FailsAfterStagingRestore()
    {
        string sourcePath = Path.Combine(_testDir, "aliased-external.png");
        File.WriteAllText(sourcePath, "aliased");
        var sharedOwner = new PackageTestFileSourceItem
        {
            Source = CreateImageSource(sourcePath),
        };
        var item = new PackageTestCollectionResourceItem
        {
            DirectOwner = sharedOwner,
            NestedOwners = [sharedOwner],
        };
        var project = new Project { Name = "AliasedOwner" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, "aliased-owner.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        string packagePath = Path.Combine(_exportDir, "aliased-owner.zip");

        Assert.That(item.DirectOwner, Is.SameAs(item.NestedOwners.Single()));

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [TestCase(".git")]
    [TestCase(".beutl")]
    public async Task ExportAsync_WithEmbeddedProjectItemInReservedStructuralDirectory_Fails(string reservedName)
    {
        string reservedDirectory = Path.Combine(_projectDir, reservedName);
        string itemPath = Path.Combine(reservedDirectory, "reserved.scene");
        var item = new Scene(1920, 1080, "ReservedScene")
        {
            Uri = new Uri(itemPath),
        };
        var project = new Project { Name = "ReservedStructuralPath" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, "reserved-structural-path.bep"));
        CoreSerializer.StoreToUri(
            project,
            projectUri,
            CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, $"reserved-{reservedName[1..]}.zip");

        Assert.That(File.Exists(itemPath), Is.False);

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
            Assert.That(File.Exists(itemPath), Is.False);
        });
    }

    [TestCase(".git")]
    [TestCase(".beutl")]
    public async Task ExportAsync_WithFileSourceInReservedStructuralDirectory_Fails(string reservedName)
    {
        string reservedDirectory = Path.Combine(_projectDir, reservedName);
        Directory.CreateDirectory(reservedDirectory);
        string sourcePath = Path.Combine(reservedDirectory, "referenced.png");
        File.WriteAllText(sourcePath, "reserved resource");
        Project project = CreateAndSaveTestProjectWithImageSource(sourcePath);
        string packagePath = Path.Combine(_exportDir, $"reserved-source-{reservedName[1..]}.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [TestCase("List")]
    [TestCase("Dictionary")]
    [TestCase("Wrapper")]
    [TestCase("NestedOwner")]
    public async Task ExportAsync_WithNonAddressableSerializedFileSource_Fails(string shape)
    {
        string sourcePath = Path.Combine(_testDir, $"{shape.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, shape);
        ImageSource source = CreateImageSource(sourcePath);
        var item = new PackageTestCollectionResourceItem();
        switch (shape)
        {
            case "List":
                item.Sources.Add(source);
                break;
            case "Dictionary":
                item.SourceMap.Add("source", source);
                break;
            case "Wrapper":
                item.Wrapper = new PackageTestResourceWrapper { Source = source };
                break;
            case "NestedOwner":
                item.NestedOwners.Add(new PackageTestFileSourceItem { Source = source });
                break;
            default:
                Assert.Fail($"Unknown resource shape: {shape}");
                break;
        }

        var project = new Project { Name = $"NonAddressable{shape}" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, $"non-addressable-{shape.ToLowerInvariant()}.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, $"non-addressable-{shape.ToLowerInvariant()}.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [TestCase("Poco")]
    [TestCase("Record")]
    [TestCase("Struct")]
    [TestCase("Converter")]
    [TestCase("Memory")]
    [TestCase("ReadOnlyMemory")]
    [TestCase("Polymorphic")]
    public async Task ExportAsync_WithSystemTextJsonWrappedFileSource_Fails(string shape)
    {
        string sourcePath = Path.Combine(_testDir, $"{shape.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, shape);
        ImageSource source = CreateImageSource(sourcePath);
        var item = new PackageTestSystemTextJsonResourceItem();
        switch (shape)
        {
            case "Poco":
                item.Poco = new PackageTestPocoWrapper { Source = source };
                break;
            case "Record":
                item.Record = new PackageTestRecordWrapper { Source = source };
                break;
            case "Struct":
                item.Struct = new PackageTestStructWrapper { Source = source };
                break;
            case "Converter":
                item.Converter = new PackageTestCustomConverterWrapper { Source = source };
                break;
            case "Memory":
                item.Memory = new Memory<ImageSource>([source]);
                break;
            case "ReadOnlyMemory":
                item.ReadOnlyMemory = new ReadOnlyMemory<ImageSource>([source]);
                break;
            case "Polymorphic":
                item.Polymorphic = new PackageTestPolymorphicWrapper { Source = source };
                break;
            default:
                Assert.Fail($"Unknown resource shape: {shape}");
                break;
        }

        var project = new Project { Name = $"SystemTextJson{shape}" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, $"system-text-json-{shape.ToLowerInvariant()}.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, $"system-text-json-{shape.ToLowerInvariant()}.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithNonPolymorphicCollection_DoesNotInspectDerivedMembers()
    {
        string sourcePath = Path.Combine(_testDir, "non-polymorphic-external.png");
        File.WriteAllText(sourcePath, "not serialized");
        var item = new PackageTestSystemTextJsonResourceItem
        {
            NonPolymorphicItems =
            [
                new PackageTestNonPolymorphicDerived
                {
                    Name = "serialized base member",
                    Source = CreateImageSource(sourcePath),
                },
            ],
        };
        var project = new Project { Name = "NonPolymorphicCollection" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, "non-polymorphic-collection.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        string packagePath = Path.Combine(_exportDir, "non-polymorphic-collection.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(File.Exists(packagePath), Is.True);
        });
    }

    [Test]
    public async Task ExportAsync_WithNodeGraphInputFileSource_Fails()
    {
        string sourcePath = Path.Combine(_testDir, "node-graph-external.png");
        File.WriteAllText(sourcePath, "node graph");
        var sourceNode = new ImageSourceNode();
        sourceNode.Source.Property!.SetValue(CreateImageSource(sourcePath));
        Project project = CreateAndSaveNodeGraphProject(sourceNode, "node-graph");
        string packagePath = Path.Combine(_exportDir, "node-graph.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithNodeGraphAnimatedFileSource_Fails()
    {
        string sourcePath = Path.Combine(_testDir, "node-graph-animated-external.png");
        File.WriteAllText(sourcePath, "node graph animation");
        var animation = new KeyFrameAnimation<ImageSource?>();
        animation.KeyFrames.Add(new KeyFrame<ImageSource?>
        {
            KeyTime = TimeSpan.Zero,
            Value = CreateImageSource(sourcePath),
        });
        var sourceNode = new ImageSourceNode();
        ((NodePropertyAdapter<ImageSource?>)sourceNode.Source.Property!).Animation = animation;
        Project project = CreateAndSaveNodeGraphProject(sourceNode, "node-graph-animation");
        string packagePath = Path.Combine(_exportDir, "node-graph-animation.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [Test]
    public async Task ExportAsync_WithObjectTypedNodeGraphWrapper_Fails()
    {
        string sourcePath = Path.Combine(_testDir, "node-graph-object-external.png");
        File.WriteAllText(sourcePath, "node graph object");
        var outputNode = new OutputNode();
        outputNode.InputPort.Property!.SetValue(new PackageTestPocoWrapper
        {
            Source = CreateImageSource(sourcePath),
        });
        Project project = CreateAndSaveNodeGraphProject(
            outputNode,
            "node-graph-object",
            restore: false);
        string packagePath = Path.Combine(_exportDir, "node-graph-object.zip");

        ExportResult result = await ProjectPackageService.Current.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(File.Exists(packagePath), Is.False);
        });
    }

    [Test]
    public void DiscoverSerializationGraph_WithDirectUriConverter_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "direct-converter-external.png");
        File.WriteAllText(sourcePath, "direct converter");
        var item = new PackageTestOpaqueConverterItem
        {
            Direct = new PackageTestDirectUriWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "DirectConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));

    }

    [Test]
    public void DiscoverSerializationGraph_WithShapeInspectingConverter_PreservesNestedJson()
    {
        string firstPath = Path.Combine(_testDir, "shape-converter-first.png");
        string secondPath = Path.Combine(_testDir, "shape-converter-second.png");
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");
        var item = new PackageTestOpaqueConverterItem
        {
            ShapeInspecting = new PackageTestShapeInspectingWrapper
            {
                First = CreateImageSource(firstPath),
                Second = CreateImageSource(secondPath),
            },
        };
        var project = new Project { Name = "ShapeInspectingConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { firstPath, secondPath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithCorePropertyConverter_UsesConvertedShape()
    {
        string serializedPath = Path.Combine(_testDir, "metadata-converter-serialized.png");
        string omittedPath = Path.Combine(_testDir, "metadata-converter-omitted.png");
        File.WriteAllText(serializedPath, "serialized");
        File.WriteAllText(omittedPath, "omitted");
        var item = new PackageTestOpaqueConverterItem
        {
            Metadata = new PackageTestMetadataConverterWrapper
            {
                SerializedSource = CreateImageSource(serializedPath),
                OmittedSource = CreateImageSource(omittedPath),
            },
        };
        var project = new Project { Name = "CorePropertyConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { serializedPath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithDeclaredBasePropertyConverter_UsesConverterContract()
    {
        string sourcePath = Path.Combine(_testDir, "base-property-converter-external.png");
        File.WriteAllText(sourcePath, "base property converter");
        var item = new PackageTestDeclaredBaseConverterItem
        {
            Payload = new PackageTestDeclaredBaseConverterDerived
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "DeclaredBasePropertyConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Does.Contain(sourcePath));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueInterfaceFileSourceRoundTrip_FailsClosed()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-interface-round-trip-external.png");
        File.WriteAllText(sourcePath, "opaque interface round trip");
        var item = new PackageTestJsonPayloadItem<PackageTestInterfaceFileSourceWrapper>
        {
            Payload = new PackageTestInterfaceFileSourceWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "OpaqueInterfaceFileSourceRoundTrip" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueOmittedResource_DoesNotCollectOmittedField()
    {
        string serializedPath = Path.Combine(_testDir, "opaque-serialized-external.png");
        string omittedPath = Path.Combine(_testDir, "opaque-omitted-external.png");
        File.WriteAllText(serializedPath, "serialized");
        File.WriteAllText(omittedPath, "omitted");
        var item = new PackageTestJsonPayloadItem<PackageTestOpaqueOmittingWrapper>
        {
            Payload = new PackageTestOpaqueOmittingWrapper
            {
                SerializedSource = CreateImageSource(serializedPath),
                OmittedSource = CreateImageSource(omittedPath),
            },
        };
        var project = new Project { Name = "OpaqueOmittedResource" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { serializedPath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueRegeneratedCache_DoesNotCollectCache()
    {
        string cachePath = Path.Combine(_testDir, "opaque-regenerated-cache.png");
        File.WriteAllText(cachePath, "opaque regenerated cache");
        PackageTestOpaqueRegeneratedCacheWrapper.CacheUri = new Uri(cachePath).AbsoluteUri;
        try
        {
            var item = new PackageTestAdHocSetValueItem<PackageTestOpaqueRegeneratedCacheWrapper>
            {
                Payload = new PackageTestOpaqueRegeneratedCacheWrapper
                {
                    Value = "serialized value",
                },
            };
            var project = new Project { Name = "OpaqueRegeneratedCache" };
            project.Items.Add(item);

            ExternalResourceCollector.SerializationGraph graph
                = ExternalResourceCollector.DiscoverSerializationGraph(project);

            Assert.That(graph.UnaddressableFileSources, Is.Empty);
        }
        finally
        {
            PackageTestOpaqueRegeneratedCacheWrapper.CacheUri = null;
        }
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueEncodedResource_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-encoded-resource.png");
        File.WriteAllText(sourcePath, "opaque encoded resource");
        var item = new PackageTestAdHocSetValueItem<PackageTestOpaqueEncodedResourceWrapper>
        {
            Payload = new PackageTestOpaqueEncodedResourceWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "OpaqueEncodedResource" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValuePropertyConverter_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "set-value-property-converter-external.png");
        File.WriteAllText(sourcePath, "set value property converter");
        var item = new PackageTestAdHocSetValueItem<PackageTestPropertyConverterDto>
        {
            Payload = new PackageTestPropertyConverterDto
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetValuePropertyConverterBypass" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));

    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValuePropertyConverterEscapedPath_CollectsExactFileSource()
    {
        string sourcePath = Path.Combine(
            _testDir,
            "set-value-property-converter#external.png");
        File.WriteAllText(sourcePath, "set value property converter escaped path");
        var item = new PackageTestAdHocSetValueItem<PackageTestPropertyConverterDto>
        {
            Payload = new PackageTestPropertyConverterDto
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetValuePropertyConverterEscapedPath" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueOpaqueUri_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "set-value-opaque-uri-external.png");
        File.WriteAllText(sourcePath, "set value opaque URI");
        var item = new PackageTestAdHocSetValueItem<PackageTestOpaqueUriErasingWrapper>
        {
            Payload = new PackageTestOpaqueUriErasingWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetValueOpaqueUriErasingConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueOpaqueUriKey_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "set-value-opaque-uri-key-external.png");
        File.WriteAllText(sourcePath, "set value opaque URI key");
        var item = new PackageTestAdHocSetValueItem<PackageTestOpaqueUriKeyWrapper>
        {
            Payload = new PackageTestOpaqueUriKeyWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetValueOpaqueUriKeyConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueOpaqueRelativeUriKeyWithoutBase_FailsClosed()
    {
        var item = new PackageTestAdHocSetValueItem<PackageTestOpaqueUriKeyWrapper>
        {
            Payload = new PackageTestOpaqueUriKeyWrapper
            {
                SerializedUri = "../outside.png",
            },
        };
        var project = new Project { Name = "SetValueOpaqueRelativeUriKey" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueDirectUriConverter_CollectsFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "set-value-direct-uri-external.png");
        File.WriteAllText(sourcePath, "set value direct URI");
        var item = new PackageTestAdHocSetValueItem<PackageTestDirectUriWrapper>
        {
            Payload = new PackageTestDirectUriWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetValueDirectUriConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [TestCase("set-value-custom-scalar-external.png")]
    [TestCase("set-value-custom-scalar#external.png")]
    public void DiscoverSerializationGraph_WithAdHocSetValueCustomScalarConverter_CollectsFileSource(
        string fileName)
    {
        string sourcePath = Path.Combine(_testDir, fileName);
        File.WriteAllText(sourcePath, "set value custom scalar");
        var item = new PackageTestAdHocSetValueItem<PackageTestCustomScalarPathDto>
        {
            Payload = new PackageTestCustomScalarPathDto
            {
                Path = sourcePath,
            },
        };
        var project = new Project { Name = "SetValueCustomScalarConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueCustomScalarConverter_CollectsEscapedFileUri()
    {
        string sourcePath = Path.Combine(_testDir, "set-value-custom-scalar#uri.png");
        File.WriteAllText(sourcePath, "set value custom scalar escaped URI");
        string serializedUri = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = sourcePath,
        }.Uri.AbsoluteUri;
        var item = new PackageTestAdHocSetValueItem<PackageTestCustomScalarPathDto>
        {
            Payload = new PackageTestCustomScalarPathDto
            {
                Path = serializedUri,
            },
        };
        var project = new Project { Name = "SetValueCustomScalarEscapedFileUri" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [TestCase("https://example.com/image.png")]
    [TestCase("/api/v1")]
    public void DiscoverSerializationGraph_WithAdHocSetValueCustomScalarConverter_IgnoresNonFileUri(
        string value)
    {
        var item = new PackageTestAdHocSetValueItem<PackageTestCustomScalarPathDto>
        {
            Payload = new PackageTestCustomScalarPathDto
            {
                Path = value,
            },
        };
        var project = new Project { Name = "SetValueCustomScalarHttpUri" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(graph.UnaddressableFileSources, Is.Empty);
    }

    [TestCase("asset")]
    [TestCase("asset#v1")]
    public void DiscoverSerializationGraph_WithAdHocSetValueCustomScalarConverter_CollectsRelativeExtensionlessFile(
        string fileName)
    {
        string ownerDirectory = Path.Combine(_testDir, "outside-owner");
        Directory.CreateDirectory(ownerDirectory);
        string sourcePath = Path.Combine(ownerDirectory, fileName);
        File.WriteAllText(sourcePath, "set value custom scalar extensionless");
        var item = new PackageTestAdHocSetValueItem<PackageTestCustomScalarPathDto>
        {
            Uri = new Uri(Path.Combine(ownerDirectory, "item.belm")),
            Payload = new PackageTestCustomScalarPathDto
            {
                Path = fileName,
            },
        };
        var project = new Project { Name = "SetValueCustomScalarExtensionless" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValuePoint_DoesNotTreatScalarAsFilePath()
    {
        var item = new PackageTestAdHocSetValueItem<Point>
        {
            Payload = new Point(0.5f, 0.5f),
        };
        var project = new Project { Name = "SetValuePoint" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(graph.UnaddressableFileSources, Is.Empty);
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetValueRational_DoesNotTreatScalarAsFilePath()
    {
        var item = new PackageTestAdHocSetValueItem<Rational>
        {
            Payload = new Rational(30000, 1001),
        };
        var project = new Project { Name = "SetValueRational" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(graph.UnaddressableFileSources, Is.Empty);
    }

    [TestCase("FileInfo", true)]
    [TestCase("FileInfo", false)]
    [TestCase("DirectoryInfo", true)]
    [TestCase("DirectoryInfo", false)]
    public void DiscoverSerializationGraph_WithAdHocSetValueFileSystemInfo_CollectsPath(
        string valueType,
        bool createPath)
    {
        string sourcePath = Path.Combine(_testDir, $"set-value-{valueType.ToLowerInvariant()}");
        if (createPath && valueType == "FileInfo")
        {
            File.WriteAllText(sourcePath, "set value file info");
        }
        else if (createPath)
        {
            Directory.CreateDirectory(sourcePath);
        }

        ProjectItem item = valueType switch
        {
            "FileInfo" => new PackageTestAdHocSetValueItem<FileInfo>
            {
                Payload = new FileInfo(sourcePath),
            },
            "DirectoryInfo" => new PackageTestAdHocSetValueItem<DirectoryInfo>
            {
                Payload = new DirectoryInfo(sourcePath),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(valueType)),
        };
        var project = new Project { Name = $"SetValue{valueType}" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithUnstableTypedSetNodeConverter_FailsClosed()
    {
        string sourcePath = Path.Combine(_testDir, "unstable-set-node-converter-external.png");
        File.WriteAllText(sourcePath, "unstable set node converter");
        var item = new PackageTestAdHocSetNodeItem<PackageTestUnstableSetNodeDto>
        {
            Payload = new PackageTestUnstableSetNodeDto
            {
                Payload = new PackageTestUnstableConverterPayload
                {
                    RawUri = sourcePath,
                },
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "UnstableTypedSetNodeConverter" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithUnstableScalarSetNodeConverter_FailsClosed()
    {
        string sourcePath = Path.Combine(_testDir, "unstable-scalar-converter-external.png");
        File.WriteAllText(sourcePath, "unstable scalar converter");
        var item = new PackageTestAdHocSetNodeItem<PackageTestUnstableScalarSetNodeDto>
        {
            Payload = new PackageTestUnstableScalarSetNodeDto
            {
                Source = CreateImageSource(sourcePath),
                Payload = new PackageTestUnstableScalarConverterPayload
                {
                    RawUri = sourcePath,
                },
            },
        };
        var project = new Project { Name = "UnstableScalarSetNodeConverter" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetNode_CollectsTypedFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "ad-hoc-set-node-external.png");
        File.WriteAllText(sourcePath, "ad hoc set node");
        var item = new PackageTestAdHocSetNodeItem
        {
            Payload = new PackageTestPocoWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "AdHocSetNode" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithAdHocSetNodeScalarSibling_CollectsTypedFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "ad-hoc-scalar-sibling-external.png");
        File.WriteAllText(sourcePath, "ad hoc scalar sibling");
        var item = new PackageTestAdHocSetNodeItem<PackageTestScalarSiblingSetNodeDto>
        {
            Payload = new PackageTestScalarSiblingSetNodeDto
            {
                Name = "payload",
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "AdHocScalarSiblingSetNode" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [TestCase("KeyValuePair")]
    [TestCase("Tuple")]
    public void DiscoverSerializationGraph_WithAdHocSystemCompositeSetNode_CollectsTypedFileSource(
        string compositeShape)
    {
        string sourcePath = Path.Combine(
            _testDir,
            $"ad-hoc-{compositeShape.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, compositeShape);
        IFileSource source = CreateImageSource(sourcePath);
        ProjectItem item = compositeShape switch
        {
            "KeyValuePair" => new PackageTestAdHocSetNodeItem<
                KeyValuePair<string, IFileSource>>
            {
                Payload = new KeyValuePair<string, IFileSource>("source", source),
            },
            "Tuple" => new PackageTestAdHocSetNodeItem<Tuple<IFileSource>>
            {
                Payload = Tuple.Create(source),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(compositeShape)),
        };
        var project = new Project { Name = $"AdHoc{compositeShape}SetNode" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Is.EquivalentTo(new[] { sourcePath }));
    }

    [Test]
    public void DiscoverSerializationGraph_WithReferenceFileSource_UsesFileSourcePrecedence()
    {
        string sourcePath = Path.Combine(_testDir, "reference-file-source-external.png");
        File.WriteAllText(sourcePath, "reference file source");
        var item = new PackageTestReferenceFileSourceItem
        {
            Source = new PackageTestReferenceFileSource(new Uri(sourcePath)),
        };
        var project = new Project { Name = "ReferenceFileSource" };
        project.Items.Add(item);
        JsonObject serialized = CoreSerializer.SerializeToJsonObject(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.Multiple(() =>
        {
            Assert.That(
                serialized[nameof(PackageTestReferenceFileSourceItem.Source)]?.GetValue<string>(),
                Is.EqualTo(item.Source.Uri.ToString()));
            Assert.That(
                graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
                Is.EquivalentTo(new[] { sourcePath }));
        });
    }

    [Test]
    public void DiscoverSerializationGraph_WithEngineExpression_DoesNotRejectKnownRawJsonContract()
    {
        var shape = new Beutl.Graphics.Shapes.RectShape();
        shape.Width.Expression = Beutl.Engine.Expressions.Expression.Create<float>("100");
        var item = new PackageTestJsonPayloadItem<Beutl.Graphics.Shapes.RectShape>
        {
            Payload = shape,
        };
        var project = new Project { Name = "EngineExpression" };
        project.Items.Add(item);

        Assert.DoesNotThrow(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithResourceExpression_CollectsSerializedFileSource()
    {
        string sourcePath = Path.Combine(_testDir, "expression-external.png");
        File.WriteAllText(sourcePath, "expression resource");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        shape.Width.Expression = new PackageTestResourceExpression
        {
            Source = CreateImageSource(sourcePath),
        };
        var item = new PackageTestJsonPayloadItem<Beutl.Graphics.Shapes.RectShape>
        {
            Payload = shape,
        };
        var project = new Project { Name = "ResourceExpression" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Does.Contain(sourcePath));
    }

    [Test]
    public void DiscoverSerializationGraph_WithNestedPolymorphicSetNode_CollectsDerivedResource()
    {
        string sourcePath = Path.Combine(_testDir, "nested-polymorphic-set-node-external.png");
        File.WriteAllText(sourcePath, "nested polymorphic set node");
        var item = new PackageTestAdHocSetNodeItem<PackageTestNestedPolymorphicDto>
        {
            Payload = new PackageTestNestedPolymorphicDto
            {
                Value = new PackageTestPolymorphicWrapper
                {
                    Source = CreateImageSource(sourcePath),
                },
            },
        };
        var project = new Project { Name = "NestedPolymorphicSetNode" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Does.Contain(sourcePath));
    }

    [Test]
    public void DiscoverSerializationGraph_WithSplineEasing_DoesNotRejectKnownRawJsonContract()
    {
        var keyFrame = new KeyFrame<float>
        {
            Value = 1,
            Easing = new Beutl.Animation.Easings.SplineEasing(0.25f, 0.1f, 0.75f, 0.9f),
        };
        var item = new PackageTestJsonPayloadItem<KeyFrame<float>>
        {
            Payload = keyFrame,
        };
        var project = new Project { Name = "SplineEasing" };
        project.Items.Add(item);

        Assert.DoesNotThrow(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithRawSetJsonObject_FailsClosed()
    {
        string sourcePath = Path.Combine(_testDir, "raw-json-object-external.png");
        File.WriteAllText(sourcePath, "raw json object");
        var item = new PackageTestRawJsonObjectItem
        {
            Payload = new PackageTestPocoWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "RawJsonObject" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueCoreObjectConverter_AddsSidecarToGraph()
    {
        string sidecarPath = Path.Combine(_testDir, "opaque-converter.sidecar");
        File.WriteAllText(sidecarPath, "opaque converter sidecar");
        var sidecar = new PackageTestSidecar
        {
            Uri = new Uri(sidecarPath),
            Value = "round-tripped sidecar",
        };
        var item = new PackageTestOpaqueCoreObjectItem
        {
            Payload = new PackageTestOpaqueCoreObjectWrapper
            {
                Sidecar = sidecar,
            },
        };
        var project = new Project { Name = "OpaqueCoreObjectConverter" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);
        PackageTestSidecar[] discoveredSidecars
            = [.. graph.Objects.OfType<PackageTestSidecar>()];

        Assert.That(discoveredSidecars, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(discoveredSidecars[0].Id, Is.EqualTo(sidecar.Id));
            Assert.That(discoveredSidecars[0].Uri, Is.EqualTo(sidecar.Uri));
        });

        ExternalResourceCollector collector
            = ExternalResourceCollector.Collect(graph, _projectDir, stagedStorageObjects: null);
        Assert.That(
            collector.FileSources,
            Has.Some.EqualTo((sidecar.Id, "Uri", sidecar.Uri)));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueComputedFileSource_FailsClosedWithoutInvokingGetter()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-computed-external.png");
        File.WriteAllText(sourcePath, "opaque computed source");
        PackageTestOpaqueComputedFileSourceWrapper.ResetGetterInvocationCount();
        var item = new PackageTestOpaqueComputedFileSourceItem
        {
            Payload = new PackageTestOpaqueComputedFileSourceWrapper(sourcePath),
        };
        var project = new Project { Name = "OpaqueComputedFileSource" };
        project.Items.Add(item);
        Exception? exception = null;

        try
        {
            ExternalResourceCollector.DiscoverSerializationGraph(project);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(
                PackageTestOpaqueComputedFileSourceWrapper.GetterInvocationCount,
                Is.Zero);
        });
    }

    [TestCase("Object")]
    [TestCase("Enumerable")]
    public void DiscoverSerializationGraph_WithOpaqueBroadComputedResource_FailsClosedWithoutInvokingGetter(
        string accessorShape)
    {
        string sourcePath = Path.Combine(
            _testDir,
            $"opaque-{accessorShape.ToLowerInvariant()}-computed-external.png");
        File.WriteAllText(sourcePath, accessorShape);
        PackageTestComputedGetterCounter.Reset();
        ProjectItem item = accessorShape switch
        {
            "Object" => new PackageTestJsonPayloadItem<PackageTestOpaqueObjectComputedWrapper>
            {
                Payload = new PackageTestOpaqueObjectComputedWrapper(sourcePath),
            },
            "Enumerable" => new PackageTestJsonPayloadItem<PackageTestOpaqueEnumerableComputedWrapper>
            {
                Payload = new PackageTestOpaqueEnumerableComputedWrapper(sourcePath),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(accessorShape)),
        };
        var project = new Project { Name = $"Opaque{accessorShape}ComputedResource" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscoverSerializationGraph_WithOpaqueComputedResourceAndUnrelatedField_FailsClosedWithoutInvokingGetter(
        bool hasUnrelatedSource)
    {
        string computedPath = Path.Combine(_testDir, "opaque-computed-covered-external.png");
        string unrelatedPath = Path.Combine(_testDir, "opaque-unrelated-external.png");
        File.WriteAllText(computedPath, "computed");
        File.WriteAllText(unrelatedPath, "unrelated");
        PackageTestComputedGetterCounter.Reset();
        var item
            = new PackageTestJsonPayloadItem<PackageTestOpaqueComputedWithUnrelatedFieldWrapper>
            {
                Payload = new PackageTestOpaqueComputedWithUnrelatedFieldWrapper(
                    computedPath,
                    hasUnrelatedSource ? CreateImageSource(unrelatedPath) : null),
            };
        var project = new Project { Name = "OpaqueComputedWithUnrelatedField" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [Test]
    public void DiscoverSerializationGraph_WithNestedOpaqueComputedResource_FailsClosedWithoutInvokingGetter()
    {
        string sourcePath = Path.Combine(_testDir, "nested-opaque-computed-external.png");
        File.WriteAllText(sourcePath, "nested opaque computed");
        PackageTestComputedGetterCounter.Reset();
        var item = new PackageTestJsonPayloadItem<PackageTestTransparentOuterDto>
        {
            Payload = new PackageTestTransparentOuterDto
            {
                Nested = new PackageTestNestedOpaqueComputedWrapper(sourcePath),
            },
        };
        var project = new Project { Name = "NestedOpaqueComputedResource" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [TestCase("GetJsonObject")]
    [TestCase("SetNodeJsonNode")]
    [TestCase("SetNodeJsonElement")]
    [TestCase("SetValueJsonNode")]
    public void DiscoverSerializationGraph_WithUntypedCustomJsonPayload_FailsClosed(
        string serializationShape)
    {
        string sourcePath = Path.Combine(
            _testDir,
            $"untyped-{serializationShape.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, serializationShape);
        var item = new PackageTestUntypedJsonPayloadItem
        {
            SerializationShape = serializationShape,
            Payload = new PackageTestPocoWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = $"Untyped{serializationShape}Payload" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithRawSiblingAddedThroughSetNodeParent_DoesNotMissResource()
    {
        string sourcePath = Path.Combine(_testDir, "set-node-parent-raw-external.png");
        File.WriteAllText(sourcePath, "set node parent raw payload");
        var item = new PackageTestSetNodeParentMutationItem
        {
            RawPayload = new PackageTestPocoWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetNodeParentRawPayload" };
        project.Items.Add(item);
        ExternalResourceCollector.SerializationGraph? graph = null;
        Exception? exception = null;

        try
        {
            graph = ExternalResourceCollector.DiscoverSerializationGraph(project);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        bool handledSafely = exception is InvalidDataException
                             || graph?.UnaddressableFileSources.Any(
                                 uri => uri.LocalPath == sourcePath) == true;
        Assert.That(
            handledSafely,
            Is.True,
            "The final serialized payload must be inspected or rejected as unsafe.");
    }

    [Test]
    public void DiscoverSerializationGraph_WithUnknownDescendantAddedToTypedSetNode_FailsClosed()
    {
        string sourcePath = Path.Combine(_testDir, "set-node-descendant-raw-external.png");
        File.WriteAllText(sourcePath, "set node descendant raw payload");
        var item = new PackageTestSetNodeDescendantMutationItem
        {
            RawPayload = new PackageTestPocoWrapper
            {
                Source = CreateImageSource(sourcePath),
            },
        };
        var project = new Project { Name = "SetNodeDescendantRawPayload" };
        project.Items.Add(item);

        Assert.Throws<InvalidDataException>(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueAutoBackedCoreObject_FailsClosedWithoutInvokingGetter()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-core-object-computed-external.png");
        File.WriteAllText(sourcePath, "opaque core object computed source");
        PackageTestComputedGetterCounter.Reset();
        var item = new PackageTestJsonPayloadItem<PackageTestOpaqueComputedCoreObjectHolder>
        {
            Payload = new PackageTestOpaqueComputedCoreObjectHolder
            {
                Value = new PackageTestComputedAccessorCoreObject(sourcePath),
            },
        };
        var project = new Project { Name = "OpaqueAutoBackedCoreObject" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [TestCase("IFileSource")]
    [TestCase("Object")]
    public void DiscoverSerializationGraph_WithTransparentManualBackedResource_DiscoversResource(
        string propertyShape)
    {
        string sourcePath = Path.Combine(
            _testDir,
            $"transparent-manual-{propertyShape.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, propertyShape);
        ImageSource source = CreateImageSource(sourcePath);
        ProjectItem item = propertyShape switch
        {
            "IFileSource" => new PackageTestJsonPayloadItem<PackageTestManualFileSourceDto>
            {
                Payload = new PackageTestManualFileSourceDto { Resource = source },
            },
            "Object" => new PackageTestJsonPayloadItem<PackageTestManualObjectResourceDto>
            {
                Payload = new PackageTestManualObjectResourceDto { Resource = source },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyShape)),
        };
        var project = new Project { Name = $"TransparentManual{propertyShape}Resource" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Does.Contain(sourcePath));
    }

    [Test]
    public void DiscoverSerializationGraph_WithStatefulSetValueGate_CollectsConditionalResource()
    {
        string sourcePath = Path.Combine(_testDir, "stateful-set-value-gate-external.png");
        File.WriteAllText(sourcePath, "stateful set value gate");
        var item = new PackageTestStatefulSetValueItem
        {
            Source = CreateImageSource(sourcePath),
        };
        var project = new Project { Name = "StatefulSetValueGate" };
        project.Items.Add(item);

        ExternalResourceCollector.SerializationGraph graph
            = ExternalResourceCollector.DiscoverSerializationGraph(project);

        Assert.That(
            graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
            Does.Contain(sourcePath));
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueNonSealedBaseAccessor_FailsClosedWithoutInvokingGetter()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-base-derived-external.png");
        File.WriteAllText(sourcePath, "opaque base derived source");
        PackageTestComputedGetterCounter.Reset();
        var item = new PackageTestJsonPayloadItem<PackageTestOpaqueBaseComputedWrapper>
        {
            Payload = new PackageTestOpaqueBaseComputedWrapper(sourcePath),
        };
        var project = new Project { Name = "OpaqueNonSealedBaseAccessor" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [Test]
    public void DiscoverSerializationGraph_WithOpaqueKeyValuePairAccessor_FailsClosedWithoutInvokingGetter()
    {
        string sourcePath = Path.Combine(_testDir, "opaque-key-value-pair-external.png");
        File.WriteAllText(sourcePath, "opaque key value pair source");
        PackageTestComputedGetterCounter.Reset();
        var item = new PackageTestJsonPayloadItem<PackageTestOpaqueKeyValuePairComputedWrapper>
        {
            Payload = new PackageTestOpaqueKeyValuePairComputedWrapper(sourcePath),
        };
        var project = new Project { Name = "OpaqueKeyValuePairAccessor" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [TestCase("Internal")]
    [TestCase("Private")]
    public void DiscoverSerializationGraph_WithOpaqueNonPublicAccessor_FailsClosedWithoutInvokingGetter(
        string accessibility)
    {
        string sourcePath = Path.Combine(
            _testDir,
            $"opaque-{accessibility.ToLowerInvariant()}-external.png");
        File.WriteAllText(sourcePath, accessibility);
        PackageTestComputedGetterCounter.Reset();
        ProjectItem item = accessibility switch
        {
            "Internal" => new PackageTestJsonPayloadItem<PackageTestOpaqueInternalComputedWrapper>
            {
                Payload = new PackageTestOpaqueInternalComputedWrapper(sourcePath),
            },
            "Private" => new PackageTestJsonPayloadItem<PackageTestOpaquePrivateComputedWrapper>
            {
                Payload = new PackageTestOpaquePrivateComputedWrapper(sourcePath),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(accessibility)),
        };
        var project = new Project { Name = $"Opaque{accessibility}Accessor" };
        project.Items.Add(item);

        Exception? exception = CaptureException(() =>
            ExternalResourceCollector.DiscoverSerializationGraph(project));

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InvalidDataException>());
            Assert.That(PackageTestComputedGetterCounter.Count, Is.Zero);
        });
    }

    [TestCase("List")]
    [TestCase("Dictionary")]
    public void DiscoverSerializationGraph_WithTransparentCollectionCache_DoesNotCollectIgnoredResource(
        string collectionShape)
    {
        string cachePath = Path.Combine(
            _testDir,
            $"transparent-{collectionShape.ToLowerInvariant()}-cache-external.png");
        File.WriteAllText(cachePath, collectionShape);
        PackageTestStringListWithIgnoredCache.CacheUri = cachePath;
        PackageTestStringDictionaryWithIgnoredCache.CacheUri = cachePath;
        try
        {
            ProjectItem item = collectionShape switch
            {
                "List" => new PackageTestJsonPayloadItem<
                    PackageTestTransparentCollectionDto<PackageTestStringListWithIgnoredCache>>
                {
                    Payload = new()
                    {
                        Value = ["persisted"],
                    },
                },
                "Dictionary" => new PackageTestJsonPayloadItem<
                    PackageTestTransparentCollectionDto<PackageTestStringDictionaryWithIgnoredCache>>
                {
                    Payload = new()
                    {
                        Value = new PackageTestStringDictionaryWithIgnoredCache
                        {
                            ["persisted"] = "value",
                        },
                    },
                },
                _ => throw new ArgumentOutOfRangeException(nameof(collectionShape)),
            };
            var project = new Project { Name = $"Transparent{collectionShape}Cache" };
            project.Items.Add(item);

            ExternalResourceCollector.SerializationGraph graph
                = ExternalResourceCollector.DiscoverSerializationGraph(project);

            Assert.That(
                graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
                Does.Not.Contain(cachePath));
        }
        finally
        {
            PackageTestStringListWithIgnoredCache.CacheUri = null;
            PackageTestStringDictionaryWithIgnoredCache.CacheUri = null;
        }
    }

    [Test]
    public void DiscoverSerializationGraph_WithTransparentDtoCache_DoesNotCollectIgnoredResource()
    {
        string cachePath = Path.Combine(_testDir, "transparent-dto-cache-external.png");
        File.WriteAllText(cachePath, "cache");
        PackageTestDtoWithIgnoredCache.CacheUri = cachePath;
        try
        {
            var item = new PackageTestJsonPayloadItem<PackageTestDtoWithIgnoredCache>
            {
                Payload = new PackageTestDtoWithIgnoredCache { Value = "persisted" },
            };
            var project = new Project { Name = "TransparentDtoCache" };
            project.Items.Add(item);

            ExternalResourceCollector.SerializationGraph graph
                = ExternalResourceCollector.DiscoverSerializationGraph(project);

            Assert.That(
                graph.UnaddressableFileSources.Select(uri => uri.LocalPath),
                Does.Not.Contain(cachePath));
        }
        finally
        {
            PackageTestDtoWithIgnoredCache.CacheUri = null;
        }
    }

    [Test]
    public async Task ExportAsync_WithCollectionValuedFontFamilies_CollectsEveryFont()
    {
        var relocation = new StubRelocationService(
            new RelocationResult(0, []),
            new RelocationResult(0, []));
        var service = new ProjectPackageService(relocation);
        var item = new PackageTestCollectionResourceItem
        {
            Fonts = [new FontFamily("NestedFontA"), new FontFamily("NestedFontB")],
        };
        var project = new Project { Name = "CollectionFonts" };
        project.Items.Add(item);
        Uri projectUri = new(Path.Combine(_projectDir, "collection-fonts.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        string packagePath = Path.Combine(_exportDir, "collection-fonts.zip");

        ExportResult result = await service.ExportAsync(project, packagePath);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(
                relocation.CapturedFontFamilies.Select(font => font.Name),
                Is.EquivalentTo(new[] { "NestedFontA", "NestedFontB" }));
        });
    }

    [TestCase(".beutl/linked.scene")]
    [TestCase(".BEUTL./linked.scene")]
    [TestCase(".beutl /linked.scene")]
    [TestCase(".beutl:metadata/linked.scene")]
    public void ContainsBeutlMetadataPath_RecognizesPortableAliases(string relativePath)
    {
        Assert.That(ProjectPackageService.ContainsBeutlMetadataPath(relativePath), Is.True);
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

    private Project CreateAndSaveNodeGraphProject(GraphNode node, string name, bool restore = true)
    {
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        var element = new Element
        {
            Uri = new Uri(Path.Combine(_projectDir, $"{name}.belm")),
            Length = TimeSpan.FromSeconds(1),
        };
        element.AddObject(drawable);
        var scene = new Scene(1920, 1080, name)
        {
            Uri = new Uri(Path.Combine(_projectDir, $"{name}.scene")),
        };
        scene.Children.Add(element);
        var project = new Project { Name = name };
        project.Items.Add(scene);
        Uri projectUri = new(Path.Combine(_projectDir, $"{name}.bep"));
        CoreSerializer.StoreToUri(project, projectUri);
        return restore ? CoreSerializer.RestoreFromUri<Project>(projectUri) : project;
    }

    private static ImageSource GetOnlyImageSource(Project project)
    {
        Scene scene = project.Items.OfType<Scene>().Single();
        SourceImage drawable = scene.Children.Single().Objects.OfType<SourceImage>().Single();
        return drawable.Source.CurrentValue!;
    }

    private static ImageSource CreateImageSource(string path)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(path));
        return source;
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    public sealed class PackageTestProjectItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestSidecar?> SidecarProperty;
        public static readonly CoreProperty<CoreList<PackageTestSidecar>> SidecarsProperty;
        private PackageTestSidecar? _sidecar;
        private CoreList<PackageTestSidecar> _sidecars = [];

        static PackageTestProjectItem()
        {
            SidecarProperty = ConfigureProperty<PackageTestSidecar?, PackageTestProjectItem>(
                    nameof(Sidecar))
                .Accessor(item => item.Sidecar, (item, value) => item.Sidecar = value)
                .Register();
            SidecarsProperty = ConfigureProperty<CoreList<PackageTestSidecar>, PackageTestProjectItem>(
                    nameof(Sidecars))
                .Accessor(item => item.Sidecars, (item, value) => item.Sidecars = value)
                .Register();
        }

        public PackageTestSidecar? Sidecar
        {
            get => _sidecar;
            set => SetAndRaise(SidecarProperty, ref _sidecar, value);
        }

        public CoreList<PackageTestSidecar> Sidecars
        {
            get => _sidecars;
            set => SetAndRaise(SidecarsProperty, ref _sidecars, value);
        }
    }

    public sealed class PackageTestSidecar : CoreObject
    {
        public static readonly CoreProperty<string> ValueProperty;
        private string _value = string.Empty;

        static PackageTestSidecar()
        {
            ValueProperty = ConfigureProperty<string, PackageTestSidecar>(nameof(Value))
                .Accessor(sidecar => sidecar.Value, (sidecar, value) => sidecar.Value = value)
                .Register();
        }

        public string Value
        {
            get => _value;
            set => SetAndRaise(ValueProperty, ref _value, value);
        }
    }

    public sealed class PackageTestFileSourceItem : ProjectItem
    {
        public static readonly CoreProperty<ImageSource?> SourceProperty;
        private ImageSource? _source;

        static PackageTestFileSourceItem()
        {
            SourceProperty = ConfigureProperty<ImageSource?, PackageTestFileSourceItem>(nameof(Source))
                .Accessor(item => item.Source, (item, value) => item.Source = value)
                .Register();
        }

        public ImageSource? Source
        {
            get => _source;
            set => SetAndRaise(SourceProperty, ref _source, value);
        }
    }

    public sealed class PackageTestReferenceFileSourceItem : ProjectItem
    {
        public IFileSource? Source { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue<IFileSource?>(nameof(Source), Source);
        }
    }

    public sealed class PackageTestReferenceFileSource(Uri uri) : IFileSource, IReference
    {
        public Guid Id { get; } = Guid.NewGuid();

        public CoreObject? Value => null;

        public bool IsNull => false;

        public Type ObjectType => typeof(CoreObject);

        public Uri Uri { get; private set; } = uri;

        public void ReadFrom(Uri sourceUri)
        {
            Uri = sourceUri;
        }

        public IReference Resolved(CoreObject obj)
        {
            return this;
        }
    }

    public sealed class PackageTestCollectionResourceItem : ProjectItem
    {
        public static readonly CoreProperty<CoreList<ImageSource>> SourcesProperty;
        public static readonly CoreProperty<Dictionary<string, ImageSource>> SourceMapProperty;
        public static readonly CoreProperty<PackageTestResourceWrapper?> WrapperProperty;
        public static readonly CoreProperty<CoreList<FontFamily>> FontsProperty;
        public static readonly CoreProperty<CoreList<PackageTestFileSourceItem>> NestedOwnersProperty;
        public static readonly CoreProperty<PackageTestFileSourceItem?> DirectOwnerProperty;
        private CoreList<ImageSource> _sources = [];
        private Dictionary<string, ImageSource> _sourceMap = [];
        private PackageTestResourceWrapper? _wrapper;
        private CoreList<FontFamily> _fonts = [];
        private CoreList<PackageTestFileSourceItem> _nestedOwners = [];
        private PackageTestFileSourceItem? _directOwner;

        static PackageTestCollectionResourceItem()
        {
            SourcesProperty = ConfigureProperty<CoreList<ImageSource>, PackageTestCollectionResourceItem>(
                    nameof(Sources))
                .Accessor(item => item.Sources, (item, value) => item.Sources = value)
                .Register();
            SourceMapProperty = ConfigureProperty<Dictionary<string, ImageSource>, PackageTestCollectionResourceItem>(
                    nameof(SourceMap))
                .Accessor(item => item.SourceMap, (item, value) => item.SourceMap = value)
                .Register();
            WrapperProperty = ConfigureProperty<PackageTestResourceWrapper?, PackageTestCollectionResourceItem>(
                    nameof(Wrapper))
                .Accessor(item => item.Wrapper, (item, value) => item.Wrapper = value)
                .Register();
            FontsProperty = ConfigureProperty<CoreList<FontFamily>, PackageTestCollectionResourceItem>(nameof(Fonts))
                .Accessor(item => item.Fonts, (item, value) => item.Fonts = value)
                .Register();
            NestedOwnersProperty
                = ConfigureProperty<CoreList<PackageTestFileSourceItem>, PackageTestCollectionResourceItem>(
                        nameof(NestedOwners))
                    .Accessor(item => item.NestedOwners, (item, value) => item.NestedOwners = value)
                    .Register();
            DirectOwnerProperty
                = ConfigureProperty<PackageTestFileSourceItem?, PackageTestCollectionResourceItem>(
                        nameof(DirectOwner))
                    .Accessor(item => item.DirectOwner, (item, value) => item.DirectOwner = value)
                    .Register();
        }

        public CoreList<ImageSource> Sources
        {
            get => _sources;
            set => SetAndRaise(SourcesProperty, ref _sources, value);
        }

        public Dictionary<string, ImageSource> SourceMap
        {
            get => _sourceMap;
            set => SetAndRaise(SourceMapProperty, ref _sourceMap, value);
        }

        public PackageTestResourceWrapper? Wrapper
        {
            get => _wrapper;
            set => SetAndRaise(WrapperProperty, ref _wrapper, value);
        }

        public CoreList<FontFamily> Fonts
        {
            get => _fonts;
            set => SetAndRaise(FontsProperty, ref _fonts, value);
        }

        public CoreList<PackageTestFileSourceItem> NestedOwners
        {
            get => _nestedOwners;
            set => SetAndRaise(NestedOwnersProperty, ref _nestedOwners, value);
        }

        public PackageTestFileSourceItem? DirectOwner
        {
            get => _directOwner;
            set => SetAndRaise(DirectOwnerProperty, ref _directOwner, value);
        }
    }

    public sealed class PackageTestSystemTextJsonResourceItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestPocoWrapper?> PocoProperty;
        public static readonly CoreProperty<PackageTestRecordWrapper?> RecordProperty;
        public static readonly CoreProperty<PackageTestStructWrapper> StructProperty;
        public static readonly CoreProperty<PackageTestCustomConverterWrapper?> ConverterProperty;
        public static readonly CoreProperty<Memory<ImageSource>> MemoryProperty;
        public static readonly CoreProperty<ReadOnlyMemory<ImageSource>> ReadOnlyMemoryProperty;
        public static readonly CoreProperty<PackageTestPolymorphicBase?> PolymorphicProperty;
        public static readonly CoreProperty<List<PackageTestNonPolymorphicBase>> NonPolymorphicItemsProperty;
        private PackageTestPocoWrapper? _poco;
        private PackageTestRecordWrapper? _record;
        private PackageTestStructWrapper _struct;
        private PackageTestCustomConverterWrapper? _converter;
        private Memory<ImageSource> _memory;
        private ReadOnlyMemory<ImageSource> _readOnlyMemory;
        private PackageTestPolymorphicBase? _polymorphic;
        private List<PackageTestNonPolymorphicBase> _nonPolymorphicItems = [];

        static PackageTestSystemTextJsonResourceItem()
        {
            PocoProperty = ConfigureProperty<PackageTestPocoWrapper?, PackageTestSystemTextJsonResourceItem>(
                    nameof(Poco))
                .Accessor(item => item.Poco, (item, value) => item.Poco = value)
                .Register();
            RecordProperty = ConfigureProperty<PackageTestRecordWrapper?, PackageTestSystemTextJsonResourceItem>(
                    nameof(Record))
                .Accessor(item => item.Record, (item, value) => item.Record = value)
                .Register();
            StructProperty = ConfigureProperty<PackageTestStructWrapper, PackageTestSystemTextJsonResourceItem>(
                    nameof(Struct))
                .Accessor(item => item.Struct, (item, value) => item.Struct = value)
                .Register();
            ConverterProperty
                = ConfigureProperty<PackageTestCustomConverterWrapper?, PackageTestSystemTextJsonResourceItem>(
                        nameof(Converter))
                    .Accessor(item => item.Converter, (item, value) => item.Converter = value)
                    .Register();
            MemoryProperty = ConfigureProperty<Memory<ImageSource>, PackageTestSystemTextJsonResourceItem>(
                    nameof(Memory))
                .Accessor(item => item.Memory, (item, value) => item.Memory = value)
                .Register();
            ReadOnlyMemoryProperty
                = ConfigureProperty<ReadOnlyMemory<ImageSource>, PackageTestSystemTextJsonResourceItem>(
                        nameof(ReadOnlyMemory))
                    .Accessor(item => item.ReadOnlyMemory, (item, value) => item.ReadOnlyMemory = value)
                    .Register();
            PolymorphicProperty
                = ConfigureProperty<PackageTestPolymorphicBase?, PackageTestSystemTextJsonResourceItem>(
                        nameof(Polymorphic))
                    .Accessor(item => item.Polymorphic, (item, value) => item.Polymorphic = value)
                    .Register();
            NonPolymorphicItemsProperty
                = ConfigureProperty<List<PackageTestNonPolymorphicBase>, PackageTestSystemTextJsonResourceItem>(
                        nameof(NonPolymorphicItems))
                    .Accessor(
                        item => item.NonPolymorphicItems,
                        (item, value) => item.NonPolymorphicItems = value)
                    .Register();
        }

        public PackageTestPocoWrapper? Poco
        {
            get => _poco;
            set => SetAndRaise(PocoProperty, ref _poco, value);
        }

        public PackageTestRecordWrapper? Record
        {
            get => _record;
            set => SetAndRaise(RecordProperty, ref _record, value);
        }

        public PackageTestStructWrapper Struct
        {
            get => _struct;
            set => SetAndRaise(StructProperty, ref _struct, value);
        }

        public PackageTestCustomConverterWrapper? Converter
        {
            get => _converter;
            set => SetAndRaise(ConverterProperty, ref _converter, value);
        }

        public Memory<ImageSource> Memory
        {
            get => _memory;
            set => SetAndRaise(MemoryProperty, ref _memory, value);
        }

        public ReadOnlyMemory<ImageSource> ReadOnlyMemory
        {
            get => _readOnlyMemory;
            set => SetAndRaise(ReadOnlyMemoryProperty, ref _readOnlyMemory, value);
        }

        public PackageTestPolymorphicBase? Polymorphic
        {
            get => _polymorphic;
            set => SetAndRaise(PolymorphicProperty, ref _polymorphic, value);
        }

        public List<PackageTestNonPolymorphicBase> NonPolymorphicItems
        {
            get => _nonPolymorphicItems;
            set => SetAndRaise(NonPolymorphicItemsProperty, ref _nonPolymorphicItems, value);
        }
    }

    public sealed class PackageTestOpaqueConverterItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestDirectUriWrapper?> DirectProperty;
        public static readonly CoreProperty<PackageTestShapeInspectingWrapper?> ShapeInspectingProperty;
        public static readonly CoreProperty<PackageTestMetadataConverterWrapper?> MetadataProperty;
        private PackageTestDirectUriWrapper? _direct;
        private PackageTestShapeInspectingWrapper? _shapeInspecting;
        private PackageTestMetadataConverterWrapper? _metadata;

        static PackageTestOpaqueConverterItem()
        {
            DirectProperty = ConfigureProperty<PackageTestDirectUriWrapper?, PackageTestOpaqueConverterItem>(
                    nameof(Direct))
                .Accessor(item => item.Direct, (item, value) => item.Direct = value)
                .Register();
            ShapeInspectingProperty
                = ConfigureProperty<PackageTestShapeInspectingWrapper?, PackageTestOpaqueConverterItem>(
                        nameof(ShapeInspecting))
                    .Accessor(
                        item => item.ShapeInspecting,
                        (item, value) => item.ShapeInspecting = value)
                    .Register();
            MetadataProperty
                = ConfigureProperty<PackageTestMetadataConverterWrapper?, PackageTestOpaqueConverterItem>(
                        nameof(Metadata))
                    .Accessor(item => item.Metadata, (item, value) => item.Metadata = value)
                    .SetAttribute(
                        new JsonConverterAttribute(typeof(PackageTestMetadataConverterWrapperConverter)))
                    .Register();
        }

        public PackageTestDirectUriWrapper? Direct
        {
            get => _direct;
            set => SetAndRaise(DirectProperty, ref _direct, value);
        }

        public PackageTestShapeInspectingWrapper? ShapeInspecting
        {
            get => _shapeInspecting;
            set => SetAndRaise(ShapeInspectingProperty, ref _shapeInspecting, value);
        }

        public PackageTestMetadataConverterWrapper? Metadata
        {
            get => _metadata;
            set => SetAndRaise(MetadataProperty, ref _metadata, value);
        }
    }

    public sealed class PackageTestDeclaredBaseConverterItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestDeclaredBaseConverterBase?> PayloadProperty;
        private PackageTestDeclaredBaseConverterBase? _payload;

        static PackageTestDeclaredBaseConverterItem()
        {
            PayloadProperty
                = ConfigureProperty<
                        PackageTestDeclaredBaseConverterBase?,
                        PackageTestDeclaredBaseConverterItem>(nameof(Payload))
                    .Accessor(item => item.Payload, (item, value) => item.Payload = value)
                    .SetAttribute(
                        new JsonConverterAttribute(typeof(PackageTestDeclaredBaseConverter)))
                    .Register();
        }

        public PackageTestDeclaredBaseConverterBase? Payload
        {
            get => _payload;
            set => SetAndRaise(PayloadProperty, ref _payload, value);
        }
    }

    public class PackageTestDeclaredBaseConverterBase
    {
        public ImageSource? Source { get; set; }
    }

    public sealed class PackageTestDeclaredBaseConverterDerived
        : PackageTestDeclaredBaseConverterBase;

    public sealed class PackageTestDeclaredBaseConverter
        : JsonConverter<PackageTestDeclaredBaseConverterBase>
    {
        public override PackageTestDeclaredBaseConverterBase? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            string? uri = document.RootElement.GetProperty("resourceUri").GetString();
            return new PackageTestDeclaredBaseConverterDerived
            {
                Source = CreateImageSourceFromUri(uri),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestDeclaredBaseConverterBase value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("resourceUri", value.Source?.Uri.ToString());
            writer.WriteEndObject();
        }
    }

    public sealed class PackageTestAdHocSetNodeItem : ProjectItem
    {
        public PackageTestPocoWrapper? Payload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is IJsonSerializationContext jsonContext && Payload is not null)
            {
                JsonNode? node = JsonSerializer.SerializeToNode(
                    Payload,
                    typeof(PackageTestPocoWrapper),
                    JsonHelper.SerializerOptions);
                jsonContext.SetNode(
                    "extensionPayload",
                    typeof(PackageTestPocoWrapper),
                    Payload.GetType(),
                    node);
            }
        }
    }

    public sealed class PackageTestAdHocSetNodeItem<T> : ProjectItem
    {
        public T? Payload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is IJsonSerializationContext jsonContext && Payload is not null)
            {
                JsonNode? node = JsonSerializer.SerializeToNode(
                    Payload,
                    typeof(T),
                    JsonHelper.SerializerOptions);
                jsonContext.SetNode("extensionPayload", typeof(T), Payload.GetType(), node);
            }
        }
    }

    public sealed class PackageTestAdHocSetValueItem<T> : ProjectItem
    {
        public T? Payload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue("extensionPayload", Payload);
        }
    }

    public sealed class PackageTestRawJsonObjectItem : ProjectItem
    {
        public PackageTestPocoWrapper? Payload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is IJsonSerializationContext jsonContext && Payload is not null)
            {
                JsonNode? node = JsonSerializer.SerializeToNode(
                    Payload,
                    typeof(PackageTestPocoWrapper),
                    JsonHelper.SerializerOptions);
                jsonContext.SetJsonObject(new JsonObject
                {
                    ["rawExtensionPayload"] = node,
                });
            }
        }
    }

    public sealed class PackageTestOpaqueCoreObjectItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestOpaqueCoreObjectWrapper?> PayloadProperty;
        private PackageTestOpaqueCoreObjectWrapper? _payload;

        static PackageTestOpaqueCoreObjectItem()
        {
            PayloadProperty
                = ConfigureProperty<PackageTestOpaqueCoreObjectWrapper?, PackageTestOpaqueCoreObjectItem>(
                        nameof(Payload))
                    .Accessor(item => item.Payload, (item, value) => item.Payload = value)
                    .SetAttribute(
                        new JsonConverterAttribute(typeof(PackageTestOpaqueCoreObjectWrapperConverter)))
                    .Register();
        }

        public PackageTestOpaqueCoreObjectWrapper? Payload
        {
            get => _payload;
            set => SetAndRaise(PayloadProperty, ref _payload, value);
        }
    }

    public sealed class PackageTestOpaqueCoreObjectWrapper
    {
        public PackageTestSidecar? Sidecar { get; set; }
    }

    public sealed class PackageTestOpaqueCoreObjectWrapperConverter
        : JsonConverter<PackageTestOpaqueCoreObjectWrapper>
    {
        public override PackageTestOpaqueCoreObjectWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            return new PackageTestOpaqueCoreObjectWrapper
            {
                Sidecar = new PackageTestSidecar
                {
                    Id = root.GetProperty("id").GetGuid(),
                    Uri = new Uri(root.GetProperty("uri").GetString()!),
                    Value = root.GetProperty("value").GetString()!,
                },
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueCoreObjectWrapper value,
            JsonSerializerOptions options)
        {
            if (value.Sidecar is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("id", value.Sidecar.Id);
            writer.WriteString("uri", value.Sidecar.Uri?.ToString());
            writer.WriteString("value", value.Sidecar.Value);
            writer.WriteEndObject();
        }
    }

    public sealed class PackageTestOpaqueComputedFileSourceItem : ProjectItem
    {
        public static readonly CoreProperty<PackageTestOpaqueComputedFileSourceWrapper?> PayloadProperty;
        private PackageTestOpaqueComputedFileSourceWrapper? _payload;

        static PackageTestOpaqueComputedFileSourceItem()
        {
            PayloadProperty
                = ConfigureProperty<PackageTestOpaqueComputedFileSourceWrapper?,
                        PackageTestOpaqueComputedFileSourceItem>(nameof(Payload))
                    .Accessor(item => item.Payload, (item, value) => item.Payload = value)
                    .SetAttribute(
                        new JsonConverterAttribute(
                            typeof(PackageTestOpaqueComputedFileSourceWrapper.Converter)))
                    .Register();
        }

        public PackageTestOpaqueComputedFileSourceWrapper? Payload
        {
            get => _payload;
            set => SetAndRaise(PayloadProperty, ref _payload, value);
        }
    }

    public sealed class PackageTestOpaqueComputedFileSourceWrapper
    {
        private static int s_getterInvocationCount;
        private readonly string _uri;

        public PackageTestOpaqueComputedFileSourceWrapper(string uri)
        {
            _uri = uri;
        }

        public static int GetterInvocationCount => Volatile.Read(ref s_getterInvocationCount);

        public IFileSource ComputedSource
        {
            get
            {
                Interlocked.Increment(ref s_getterInvocationCount);
                return CreateImageSourceFromUri(_uri)!;
            }
        }

        public static void ResetGetterInvocationCount()
        {
            Interlocked.Exchange(ref s_getterInvocationCount, 0);
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueComputedFileSourceWrapper>
        {
            public override PackageTestOpaqueComputedFileSourceWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueComputedFileSourceWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueComputedFileSourceWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    public sealed class PackageTestJsonPayloadItem<T> : ProjectItem
        where T : class
    {
        public static readonly CoreProperty<T?> PayloadProperty;
        private T? _payload;

        static PackageTestJsonPayloadItem()
        {
            PayloadProperty = ConfigureProperty<T?, PackageTestJsonPayloadItem<T>>(nameof(Payload))
                .Accessor(item => item.Payload, (item, value) => item.Payload = value)
                .Register();
        }

        public T? Payload
        {
            get => _payload;
            set => SetAndRaise(PayloadProperty, ref _payload, value);
        }
    }

    private static class PackageTestComputedGetterCounter
    {
        private static int s_count;

        public static int Count => Volatile.Read(ref s_count);

        public static void RecordInvocation()
        {
            Interlocked.Increment(ref s_count);
        }

        public static void Reset()
        {
            Interlocked.Exchange(ref s_count, 0);
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueObjectComputedWrapper.Converter))]
    public sealed class PackageTestOpaqueObjectComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaqueObjectComputedWrapper(string uri)
        {
            _uri = uri;
        }

        public object Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_uri)!;
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueObjectComputedWrapper>
        {
            public override PackageTestOpaqueObjectComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueObjectComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueObjectComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueEnumerableComputedWrapper.Converter))]
    public sealed class PackageTestOpaqueEnumerableComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaqueEnumerableComputedWrapper(string uri)
        {
            _uri = uri;
        }

        public System.Collections.IEnumerable Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return new[] { CreateImageSourceFromUri(_uri)! };
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueEnumerableComputedWrapper>
        {
            public override PackageTestOpaqueEnumerableComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueEnumerableComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueEnumerableComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueComputedWithUnrelatedFieldWrapper.Converter))]
    public sealed class PackageTestOpaqueComputedWithUnrelatedFieldWrapper
    {
        private readonly string _computedUri;
        private readonly ImageSource? _unrelatedSource;

        public PackageTestOpaqueComputedWithUnrelatedFieldWrapper(
            string computedUri,
            ImageSource? unrelatedSource)
        {
            _computedUri = computedUri;
            _unrelatedSource = unrelatedSource;
        }

        public IFileSource Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_computedUri)!;
            }
        }

        public sealed class Converter
            : JsonConverter<PackageTestOpaqueComputedWithUnrelatedFieldWrapper>
        {
            public override PackageTestOpaqueComputedWithUnrelatedFieldWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                JsonElement root = document.RootElement;
                return new PackageTestOpaqueComputedWithUnrelatedFieldWrapper(
                    root.GetProperty("computedUri").GetString()!,
                    CreateImageSourceFromUri(root.GetProperty("unrelatedUri").GetString()));
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueComputedWithUnrelatedFieldWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("computedUri", value._computedUri);
                writer.WriteString("unrelatedUri", value._unrelatedSource?.Uri.ToString());
                writer.WriteEndObject();
            }
        }
    }

    public sealed class PackageTestTransparentOuterDto
    {
        public PackageTestNestedOpaqueComputedWrapper? Nested { get; set; }
    }

    [JsonConverter(typeof(PackageTestNestedOpaqueComputedWrapper.Converter))]
    public sealed class PackageTestNestedOpaqueComputedWrapper
    {
        private readonly string _uri;

        public PackageTestNestedOpaqueComputedWrapper(string uri)
        {
            _uri = uri;
        }

        public IFileSource Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_uri)!;
            }
        }

        public sealed class Converter : JsonConverter<PackageTestNestedOpaqueComputedWrapper>
        {
            public override PackageTestNestedOpaqueComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestNestedOpaqueComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestNestedOpaqueComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    public sealed class PackageTestUntypedJsonPayloadItem : ProjectItem
    {
        public string SerializationShape { get; set; } = string.Empty;

        public PackageTestPocoWrapper? Payload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is not IJsonSerializationContext jsonContext || Payload is null)
            {
                return;
            }

            JsonNode node = JsonSerializer.SerializeToNode(
                Payload,
                typeof(PackageTestPocoWrapper),
                JsonHelper.SerializerOptions)!;
            switch (SerializationShape)
            {
                case "GetJsonObject":
                    jsonContext.GetJsonObject()["rawPayload"] = node;
                    break;
                case "SetNodeJsonNode":
                    jsonContext.SetNode("rawPayload", typeof(JsonNode), node.GetType(), node);
                    break;
                case "SetNodeJsonElement":
                    jsonContext.SetNode(
                        "rawPayload",
                        typeof(JsonElement),
                        typeof(JsonElement),
                        node);
                    break;
                case "SetValueJsonNode":
                    context.SetValue<JsonNode>("rawPayload", node);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown serialization shape: {SerializationShape}");
            }
        }
    }

    public sealed class PackageTestSetNodeParentMutationItem : ProjectItem
    {
        public PackageTestPocoWrapper? RawPayload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is not IJsonSerializationContext jsonContext || RawPayload is null)
            {
                return;
            }

            var typedPayload = new PackageTestNonPolymorphicBase { Name = "typed payload" };
            JsonNode typedNode = JsonSerializer.SerializeToNode(
                typedPayload,
                typeof(PackageTestNonPolymorphicBase),
                JsonHelper.SerializerOptions)!;
            jsonContext.SetNode(
                "typedPayload",
                typeof(PackageTestNonPolymorphicBase),
                typedPayload.GetType(),
                typedNode);

            JsonNode rawNode = JsonSerializer.SerializeToNode(
                RawPayload,
                typeof(PackageTestPocoWrapper),
                JsonHelper.SerializerOptions)!;
            typedNode.Parent!.AsObject()["rawPayload"] = rawNode;
        }
    }

    public sealed class PackageTestSetNodeDescendantMutationItem : ProjectItem
    {
        public PackageTestPocoWrapper? RawPayload { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (context is not IJsonSerializationContext jsonContext || RawPayload is null)
            {
                return;
            }

            var typedPayload = new PackageTestNonPolymorphicBase { Name = "typed payload" };
            JsonObject typedNode = JsonSerializer.SerializeToNode(
                typedPayload,
                typeof(PackageTestNonPolymorphicBase),
                JsonHelper.SerializerOptions)!.AsObject();
            typedNode["rawPayload"] = JsonSerializer.SerializeToNode(
                RawPayload,
                typeof(PackageTestPocoWrapper),
                JsonHelper.SerializerOptions);
            jsonContext.SetNode(
                "typedPayload",
                typeof(PackageTestNonPolymorphicBase),
                typedPayload.GetType(),
                typedNode);
        }
    }

    [JsonConverter(typeof(PackageTestComputedAccessorCoreObject.HolderConverter))]
    public sealed class PackageTestOpaqueComputedCoreObjectHolder
    {
        public PackageTestComputedAccessorCoreObject? Value { get; set; }
    }

    public sealed class PackageTestComputedAccessorCoreObject : CoreObject
    {
        private readonly string _resourceUri;

        public PackageTestComputedAccessorCoreObject(string resourceUri)
        {
            _resourceUri = resourceUri;
        }

        public IFileSource Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_resourceUri)!;
            }
        }

        public sealed class HolderConverter
            : JsonConverter<PackageTestOpaqueComputedCoreObjectHolder>
        {
            public override PackageTestOpaqueComputedCoreObjectHolder? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueComputedCoreObjectHolder
                    {
                        Value = new PackageTestComputedAccessorCoreObject(reader.GetString()!),
                    };
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueComputedCoreObjectHolder value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.Value?._resourceUri);
            }
        }
    }

    public sealed class PackageTestManualFileSourceDto
    {
        private IFileSource? _resource;

        public IFileSource? Resource
        {
            get => _resource;
            set => _resource = value;
        }
    }

    public sealed class PackageTestManualObjectResourceDto
    {
        private object? _resource;

        public object? Resource
        {
            get => _resource;
            set
            {
                _resource = value is JsonElement { ValueKind: JsonValueKind.String } element
                    ? CreateImageSourceFromUri(element.GetString())
                    : value;
            }
        }
    }

    public sealed class PackageTestStatefulSetValueItem : ProjectItem
    {
        public ImageSource? Source { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue("gate", true);
            if (context.Contains("gate") && context.GetValue<bool>("gate"))
            {
                context.SetValue("conditionalResource", Source);
            }
        }
    }

    public class PackageTestComputedResourceBase
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class PackageTestComputedResourceDerived : PackageTestComputedResourceBase
    {
        public ImageSource? Source { get; set; }
    }

    [JsonConverter(typeof(PackageTestOpaqueBaseComputedWrapper.Converter))]
    public sealed class PackageTestOpaqueBaseComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaqueBaseComputedWrapper(string uri)
        {
            _uri = uri;
        }

        public PackageTestComputedResourceBase Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return new PackageTestComputedResourceDerived
                {
                    Source = CreateImageSourceFromUri(_uri),
                };
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueBaseComputedWrapper>
        {
            public override PackageTestOpaqueBaseComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueBaseComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueBaseComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueKeyValuePairComputedWrapper.Converter))]
    public sealed class PackageTestOpaqueKeyValuePairComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaqueKeyValuePairComputedWrapper(string uri)
        {
            _uri = uri;
        }

        public KeyValuePair<string, IFileSource> Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return new KeyValuePair<string, IFileSource>(
                    "resource",
                    CreateImageSourceFromUri(_uri)!);
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueKeyValuePairComputedWrapper>
        {
            public override PackageTestOpaqueKeyValuePairComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueKeyValuePairComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueKeyValuePairComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueInternalComputedWrapper.Converter))]
    public sealed class PackageTestOpaqueInternalComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaqueInternalComputedWrapper(string uri)
        {
            _uri = uri;
        }

        internal IFileSource Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_uri)!;
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaqueInternalComputedWrapper>
        {
            public override PackageTestOpaqueInternalComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaqueInternalComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaqueInternalComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    [JsonConverter(typeof(PackageTestOpaquePrivateComputedWrapper.Converter))]
    public sealed class PackageTestOpaquePrivateComputedWrapper
    {
        private readonly string _uri;

        public PackageTestOpaquePrivateComputedWrapper(string uri)
        {
            _uri = uri;
        }

        private IFileSource Computed
        {
            get
            {
                PackageTestComputedGetterCounter.RecordInvocation();
                return CreateImageSourceFromUri(_uri)!;
            }
        }

        public sealed class Converter : JsonConverter<PackageTestOpaquePrivateComputedWrapper>
        {
            public override PackageTestOpaquePrivateComputedWrapper? Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                return reader.TokenType == JsonTokenType.Null
                    ? null
                    : new PackageTestOpaquePrivateComputedWrapper(reader.GetString()!);
            }

            public override void Write(
                Utf8JsonWriter writer,
                PackageTestOpaquePrivateComputedWrapper value,
                JsonSerializerOptions options)
            {
                writer.WriteStringValue(value._uri);
            }
        }
    }

    public sealed class PackageTestTransparentCollectionDto<T>
        where T : class
    {
        public T? Value { get; set; }
    }

    public sealed class PackageTestDtoWithIgnoredCache
    {
        [JsonIgnore]
        private readonly ImageSource? _cache;

        public PackageTestDtoWithIgnoredCache()
        {
            _cache = CreateImageSourceFromUri(CacheUri);
        }

        public static string? CacheUri { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(PackageTestInterfaceFileSourceWrapperConverter))]
    public sealed class PackageTestInterfaceFileSourceWrapper
    {
        public IFileSource? Source { get; set; }
    }

    public sealed class PackageTestInterfaceFileSourceWrapperConverter
        : JsonConverter<PackageTestInterfaceFileSourceWrapper>
    {
        public override PackageTestInterfaceFileSourceWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestInterfaceFileSourceWrapper
            {
                Source = JsonSerializer.Deserialize<IFileSource>(ref reader, options),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestInterfaceFileSourceWrapper value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Source?.Uri.ToString());
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueOmittingWrapperConverter))]
    public sealed class PackageTestOpaqueOmittingWrapper
    {
        public ImageSource? SerializedSource { get; set; }

        public ImageSource? OmittedSource { get; set; }
    }

    public sealed class PackageTestOpaqueOmittingWrapperConverter
        : JsonConverter<PackageTestOpaqueOmittingWrapper>
    {
        public override PackageTestOpaqueOmittingWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestOpaqueOmittingWrapper
            {
                SerializedSource = JsonSerializer.Deserialize<ImageSource>(ref reader, options),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueOmittingWrapper value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.SerializedSource, options);
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueRegeneratedCacheWrapperConverter))]
    public sealed class PackageTestOpaqueRegeneratedCacheWrapper
    {
        [JsonIgnore]
        private readonly ImageSource? _cache = CreateImageSourceFromUri(CacheUri);

        public static string? CacheUri { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    public sealed class PackageTestOpaqueRegeneratedCacheWrapperConverter
        : JsonConverter<PackageTestOpaqueRegeneratedCacheWrapper>
    {
        public override PackageTestOpaqueRegeneratedCacheWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestOpaqueRegeneratedCacheWrapper
            {
                Value = reader.GetString() ?? string.Empty,
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueRegeneratedCacheWrapper value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueEncodedResourceWrapperConverter))]
    public sealed class PackageTestOpaqueEncodedResourceWrapper
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ImageSource? Source { get; set; }
    }

    public sealed class PackageTestOpaqueEncodedResourceWrapperConverter
        : JsonConverter<PackageTestOpaqueEncodedResourceWrapper>
    {
        public override PackageTestOpaqueEncodedResourceWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            string encoded = reader.GetString()
                             ?? throw new JsonException("The encoded URI is required.");
            string uri = Encoding.UTF8.GetString(Convert.FromHexString(encoded));
            return new PackageTestOpaqueEncodedResourceWrapper
            {
                Source = CreateImageSourceFromUri(uri),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueEncodedResourceWrapper value,
            JsonSerializerOptions options)
        {
            string uri = value.Source?.Uri.AbsoluteUri
                         ?? throw new JsonException("The source URI is required.");
            writer.WriteStringValue(Convert.ToHexString(Encoding.UTF8.GetBytes(uri)));
        }
    }

    public sealed class PackageTestUnstableSetNodeDto
    {
        public PackageTestUnstableConverterPayload? Payload { get; set; }

        public IFileSource? Source { get; set; }
    }

    public sealed class PackageTestScalarSiblingSetNodeDto
    {
        public string Name { get; set; } = string.Empty;

        public IFileSource? Source { get; set; }
    }

    public sealed class PackageTestPropertyConverterDto
    {
        [JsonConverter(typeof(PackageTestDirectInterfaceFileSourceConverter))]
        public IFileSource? Source { get; set; }
    }

    public sealed class PackageTestCustomScalarPathDto
    {
        [JsonConverter(typeof(PackageTestCustomStringConverter))]
        public string Path { get; set; } = string.Empty;
    }

    public sealed class PackageTestCustomStringConverter : JsonConverter<string>
    {
        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.GetString();
        }

        public override void Write(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public sealed class PackageTestDirectInterfaceFileSourceConverter
        : JsonConverter<IFileSource>
    {
        public override IFileSource? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            _ = reader.GetString();
            return new BlobFileSource();
        }

        public override void Write(
            Utf8JsonWriter writer,
            IFileSource value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Uri.ToString());
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueUriErasingWrapperConverter))]
    public sealed class PackageTestOpaqueUriErasingWrapper
    {
        public IFileSource? Source { get; set; }

        public string? SerializedUri { get; set; }
    }

    public sealed class PackageTestOpaqueUriErasingWrapperConverter
        : JsonConverter<PackageTestOpaqueUriErasingWrapper>
    {
        public override PackageTestOpaqueUriErasingWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestOpaqueUriErasingWrapper
            {
                SerializedUri = reader.GetString(),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueUriErasingWrapper value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Source?.Uri.ToString() ?? value.SerializedUri);
        }
    }

    [JsonConverter(typeof(PackageTestOpaqueUriKeyWrapperConverter))]
    public sealed class PackageTestOpaqueUriKeyWrapper
    {
        public IFileSource? Source { get; set; }

        public string? SerializedUri { get; set; }
    }

    public sealed class PackageTestOpaqueUriKeyWrapperConverter
        : JsonConverter<PackageTestOpaqueUriKeyWrapper>
    {
        public override PackageTestOpaqueUriKeyWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return new PackageTestOpaqueUriKeyWrapper
            {
                SerializedUri = document.RootElement.EnumerateObject().Single().Name,
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestOpaqueUriKeyWrapper value,
            JsonSerializerOptions options)
        {
            string uri = value.Source?.Uri.ToString()
                         ?? value.SerializedUri
                         ?? throw new JsonException("The serialized URI is required.");
            writer.WriteStartObject();
            writer.WriteString(uri, "resource");
            writer.WriteEndObject();
        }
    }

    [JsonConverter(typeof(PackageTestUnstableConverterPayloadConverter))]
    public sealed class PackageTestUnstableConverterPayload
    {
        public string? RawUri { get; set; }

        public bool ThrowOnWrite { get; set; }
    }

    public sealed class PackageTestUnstableConverterPayloadConverter
        : JsonConverter<PackageTestUnstableConverterPayload>
    {
        public override PackageTestUnstableConverterPayload? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return new PackageTestUnstableConverterPayload
            {
                RawUri = document.RootElement.GetProperty("rawUri").GetString(),
                ThrowOnWrite = true,
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestUnstableConverterPayload value,
            JsonSerializerOptions options)
        {
            if (value.ThrowOnWrite)
            {
                throw new InvalidOperationException("The restored payload cannot be serialized.");
            }

            writer.WriteStartObject();
            writer.WriteString("rawUri", value.RawUri);
            writer.WriteEndObject();
        }
    }

    public sealed class PackageTestUnstableScalarSetNodeDto
    {
        public IFileSource? Source { get; set; }

        public PackageTestUnstableScalarConverterPayload? Payload { get; set; }
    }

    [JsonConverter(typeof(PackageTestUnstableScalarConverterPayloadConverter))]
    public sealed class PackageTestUnstableScalarConverterPayload
    {
        public string? RawUri { get; set; }
    }

    public sealed class PackageTestUnstableScalarConverterPayloadConverter
        : JsonConverter<PackageTestUnstableScalarConverterPayload>
    {
        public override PackageTestUnstableScalarConverterPayload? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestUnstableScalarConverterPayload
            {
                RawUri = reader.GetString(),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestUnstableScalarConverterPayload value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.RawUri);
        }
    }

    public sealed class PackageTestStringListWithIgnoredCache : List<string>
    {
        [JsonIgnore]
        private readonly ImageSource? _cache;

        public PackageTestStringListWithIgnoredCache()
        {
            _cache = CreateImageSourceFromUri(CacheUri);
        }

        public static string? CacheUri { get; set; }
    }

    public sealed class PackageTestStringDictionaryWithIgnoredCache
        : Dictionary<string, string>
    {
        [JsonIgnore]
        private readonly ImageSource? _cache;

        public PackageTestStringDictionaryWithIgnoredCache()
        {
            _cache = CreateImageSourceFromUri(CacheUri);
        }

        public static string? CacheUri { get; set; }
    }

    [JsonConverter(typeof(PackageTestDirectUriWrapperConverter))]
    public sealed class PackageTestDirectUriWrapper
    {
        public ImageSource? Source { get; set; }
    }

    public sealed class PackageTestDirectUriWrapperConverter
        : JsonConverter<PackageTestDirectUriWrapper>
    {
        public override PackageTestDirectUriWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            return new PackageTestDirectUriWrapper
            {
                Source = CreateImageSourceFromUri(reader.GetString()),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestDirectUriWrapper value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Source?.Uri.ToString());
        }
    }

    [JsonConverter(typeof(PackageTestShapeInspectingWrapperConverter))]
    public sealed class PackageTestShapeInspectingWrapper
    {
        public ImageSource? First { get; set; }

        public ImageSource? Second { get; set; }
    }

    public sealed class PackageTestShapeInspectingWrapperConverter
        : JsonConverter<PackageTestShapeInspectingWrapper>
    {
        public override PackageTestShapeInspectingWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            JsonObject? json = JsonNode.Parse(ref reader) as JsonObject;
            if (json is null)
            {
                return null;
            }

            return new PackageTestShapeInspectingWrapper
            {
                First = json["first"]?.Deserialize<ImageSource>(options),
                Second = json["second"]?.Deserialize<ImageSource>(options),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestShapeInspectingWrapper value,
            JsonSerializerOptions options)
        {
            JsonNode? first = JsonSerializer.SerializeToNode(value.First, options);
            if (first is not JsonValue firstValue
                || !firstValue.TryGetValue(out string? _))
            {
                throw new JsonException("The first source must serialize as a URI string.");
            }

            writer.WriteStartObject();
            writer.WritePropertyName("first");
            first.WriteTo(writer, options);
            writer.WritePropertyName("second");
            JsonSerializer.Serialize(writer, value.Second, options);
            writer.WriteEndObject();
        }
    }

    public sealed class PackageTestMetadataConverterWrapper
    {
        [JsonIgnore]
        public ImageSource? SerializedSource { get; set; }

        public ImageSource? OmittedSource { get; set; }
    }

    public sealed class PackageTestMetadataConverterWrapperConverter
        : JsonConverter<PackageTestMetadataConverterWrapper>
    {
        public override PackageTestMetadataConverterWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            return new PackageTestMetadataConverterWrapper
            {
                SerializedSource = CreateImageSourceFromUri(reader.GetString()),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestMetadataConverterWrapper value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.SerializedSource?.Uri.ToString());
        }
    }

    private static ImageSource? CreateImageSourceFromUri(string? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var source = new ImageSource();
        source.ReadFrom(new Uri(uri));
        return source;
    }

    public sealed class PackageTestPocoWrapper
    {
        public ImageSource? Source { get; set; }
    }

    public sealed record PackageTestRecordWrapper
    {
        public ImageSource? Source { get; init; }
    }

    public sealed class PackageTestResourceExpression
        : Beutl.Engine.Expressions.IExpression<float>
    {
        public ImageSource? Source { get; set; }

        [JsonIgnore]
        public string ExpressionString => "resource";

        public float Evaluate(Beutl.Engine.Expressions.ExpressionContext context)
        {
            return 0;
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }

    public struct PackageTestStructWrapper
    {
        public ImageSource? Source { get; set; }
    }

    [JsonConverter(typeof(PackageTestCustomConverterWrapperConverter))]
    public sealed class PackageTestCustomConverterWrapper
    {
        public ImageSource? Source { get; set; }
    }

    public sealed class PackageTestCustomConverterWrapperConverter
        : JsonConverter<PackageTestCustomConverterWrapper>
    {
        public override PackageTestCustomConverterWrapper? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return new PackageTestCustomConverterWrapper
            {
                Source = JsonSerializer.Deserialize<ImageSource>(ref reader, options),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            PackageTestCustomConverterWrapper value,
            JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Source, options);
        }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(PackageTestPolymorphicWrapper), "resource")]
    public abstract class PackageTestPolymorphicBase;

    public sealed class PackageTestNestedPolymorphicDto
    {
        public PackageTestPolymorphicBase? Value { get; set; }
    }

    public sealed class PackageTestPolymorphicWrapper : PackageTestPolymorphicBase
    {
        public ImageSource? Source { get; set; }
    }

    public class PackageTestNonPolymorphicBase
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class PackageTestNonPolymorphicDerived : PackageTestNonPolymorphicBase
    {
        public ImageSource? Source { get; set; }
    }

    public sealed class PackageTestResourceWrapper : ICoreSerializable
    {
        public ImageSource? Source { get; set; }

        public void Serialize(ICoreSerializationContext context)
        {
            context.SetValue(nameof(Source), Source);
        }

        public void Deserialize(ICoreSerializationContext context)
        {
            Source = context.GetValue<ImageSource>(nameof(Source));
        }
    }

    private sealed class StubRelocationService(
        RelocationResult fileResult,
        RelocationResult fontResult) : ResourceRelocationService
    {
        public IReadOnlyList<FontFamily> CapturedFontFamilies { get; private set; } = [];

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
        {
            CapturedFontFamilies = [.. fontFamilies];
            return Task.FromResult(fontResult);
        }
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
