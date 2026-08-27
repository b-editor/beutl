using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiResultImporterTests
{
    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task ImportImage_StagesProjectResource()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-result-importer");
        using var bitmap = new Bitmap(2, 2);
        var importer = new AiResultImporter(
            editor.Scene,
            editor.GetRequiredService<IElementAdder>());

        ElementAddResult result = await importer.ImportImageAsync(
            bitmap,
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                0,
                "AI image"));
        HeadlessTestHelpers.Settle();

        IReadOnlyList<Element> elements = result.Elements;
        string resourcePath = elements.Single().Objects
            .OfType<Beutl.Graphics.SourceImage>()
            .Single()
            .Source.CurrentValue!.Uri.LocalPath;
        Assert.Multiple(() =>
        {
            Assert.That(resourcePath, Does.Contain(Path.Combine("resources", "ai")));
            Assert.That(File.Exists(resourcePath), Is.True);
            Assert.That(elements[0].Name, Is.EqualTo("AI image"));
            Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(resourcePath)!, "*.tmp"), Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task ImportVideoBytes_StagesAndImportsEveryProducedElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-importer");
        var adder = new CapturingElementAdder(producedElementCount: 2);
        var importer = new AiResultImporter(editor.Scene, adder);

        ElementAddResult result = await importer.ImportVideoAsync(
            new byte[] { 1, 2, 3, 4 },
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                0,
                "AI video"));

        Assert.Multiple(() =>
        {
            Assert.That(adder.StagedPath, Does.EndWith(".mp4"));
            Assert.That(File.ReadAllBytes(adder.StagedPath!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(result.Elements, Has.Count.EqualTo(2));
        });
    }

    [AvaloniaTest]
    public async Task ImportVideoPath_PreservesWebmExtension()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-webm-importer");
        var adder = new CapturingElementAdder(producedElementCount: 1);
        var importer = new AiResultImporter(editor.Scene, adder);
        string sourcePath = Path.Combine(Path.GetTempPath(), $"source-{Guid.NewGuid():N}.webm");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        try
        {
            await importer.ImportVideoAsync(
                sourcePath,
                new AiResultImportOptions(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(4),
                    0,
                    "AI video"));

            Assert.That(adder.StagedPath, Does.EndWith(".webm"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [AvaloniaTest]
    public async Task RejectedVideoBatch_RemovesStagedProjectResource()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-rejected-importer");
        var adder = new CapturingElementAdder(producedElementCount: 0);
        var importer = new AiResultImporter(editor.Scene, adder);

        ElementAddResult result = await importer.ImportVideoAsync(
            new byte[] { 1, 2, 3, 4 },
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(4),
                0,
                "AI video"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failure, Is.TypeOf<ElementMaterializationFailure>());
            Assert.That(result.Elements, Is.Empty);
            Assert.That(adder.StagedPath, Is.Not.Null);
            Assert.That(File.Exists(adder.StagedPath), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task ClosingUnsavedScene_RemovesOnlyItsOwnedTemporaryDirectory()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-unsaved-resource-cleanup");
        var tab = TestShell.Editor.SelectedTabItem.Value!;
        Scene scene = editor.Scene;
        scene.Uri = null;
        var adder = new CapturingElementAdder(producedElementCount: 1);
        var importer = new AiResultImporter(scene, adder);
        string unrelatedDirectory = AiResultImporter.GetUnsavedSceneDirectory(Guid.NewGuid());
        string unrelatedFile = Path.Combine(unrelatedDirectory, "keep.txt");
        Directory.CreateDirectory(unrelatedDirectory);
        await File.WriteAllTextAsync(unrelatedFile, "unrelated");

        try
        {
            await importer.ImportVideoAsync(
                new byte[] { 1, 2, 3, 4 },
                new AiResultImportOptions(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(4),
                    0,
                    "AI video"));
            string ownedDirectory = AiResultImporter.GetUnsavedSceneDirectory(scene.Id);
            Assert.That(File.Exists(adder.StagedPath), Is.True);

            await TestShell.Editor.CloseTabItem(tab);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(ownedDirectory), Is.False);
                Assert.That(File.Exists(unrelatedFile), Is.True);
            }
        }
        finally
        {
            if (Directory.Exists(unrelatedDirectory))
                Directory.Delete(unrelatedDirectory, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task ClosingSavedScene_RemovesTemporaryResourcesAfterHistoryIsDiscarded()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-saved-resource-preservation");
        var tab = TestShell.Editor.SelectedTabItem.Value!;
        Scene scene = editor.Scene;
        Uri savedUri = scene.Uri!;
        scene.Uri = null;
        var adder = new CapturingElementAdder(producedElementCount: 1);
        var importer = new AiResultImporter(scene, adder);
        string ownedDirectory = AiResultImporter.GetUnsavedSceneDirectory(scene.Id);

        try
        {
            await importer.ImportVideoAsync(
                new byte[] { 1, 2, 3, 4 },
                new AiResultImportOptions(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(4),
                    0,
                    "AI video"));
            string resourcePath = adder.StagedPath!;
            scene.Uri = savedUri;

            await TestShell.Editor.CloseTabItem(tab);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(ownedDirectory), Is.False);
                Assert.That(File.Exists(resourcePath), Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(ownedDirectory))
                Directory.Delete(ownedDirectory, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task ImportImage_UnsavedSceneUsesRealAdderAndRehomesItsSidecarOnFirstSave()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-unsaved-real-adder");
        EditorTabItem tab = TestShell.Editor.SelectedTabItem.Value!;
        Scene scene = editor.Scene;
        Uri savedSceneUri = scene.Uri!;
        scene.Uri = null;
        string ownedDirectory = AiResultImporter.GetUnsavedSceneDirectory(scene.Id);
        using var bitmap = new Bitmap(2, 2);
        var importer = new AiResultImporter(
            scene,
            editor.GetRequiredService<IElementAdder>());

        try
        {
            ElementAddResult result = await importer.ImportImageAsync(
                bitmap,
                new AiResultImportOptions(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    0,
                    "Unsaved AI image"));
            HeadlessTestHelpers.Settle();

            Assert.That(
                result.IsSuccess,
                Is.True,
                $"{result.Failure?.Message} {result.Failure?.Exception}");
            Element element = result.Elements.Single();
            Uri unsavedSidecar = element.Uri!;
            string resourcePath = element.Objects
                .OfType<Beutl.Graphics.SourceImage>()
                .Single()
                .Source.CurrentValue!.Uri.LocalPath;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(
                    UnsavedSceneStorage.OwnsPath(scene.Id, unsavedSidecar.LocalPath),
                    Is.True);
                Assert.That(File.Exists(unsavedSidecar.LocalPath), Is.True);
                Assert.That(File.Exists(resourcePath), Is.True);
            }

            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(scene.Children, Is.Empty);
            scene.Uri = savedSceneUri;
            Assert.That(await editor.Commands!.OnSave(), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(unsavedSidecar.LocalPath), Is.True,
                    "The redo stack still owns the unsaved element sidecar.");
                Assert.That(File.Exists(resourcePath), Is.True,
                    "The redo stack still owns the imported resource.");
            }

            Assert.That(editor.HistoryManager.Redo(), Is.True);
            Assert.That(scene.Children.Single(), Is.SameAs(element));
            Assert.That(await editor.Commands.OnSave(), Is.True);

            Uri savedSidecar = element.Uri!;
            string savedResourcePath = element.Objects
                .OfType<Beutl.Graphics.SourceImage>()
                .Single()
                .Source.CurrentValue!.Uri.LocalPath;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(savedSidecar, Is.Not.EqualTo(unsavedSidecar));
                Assert.That(
                    Path.GetDirectoryName(savedSidecar.LocalPath),
                    Is.EqualTo(Path.GetDirectoryName(savedSceneUri.LocalPath)));
                Assert.That(File.Exists(savedSidecar.LocalPath), Is.True);
                Assert.That(File.Exists(unsavedSidecar.LocalPath), Is.False);
                Assert.That(savedResourcePath, Is.Not.EqualTo(resourcePath));
                Assert.That(savedResourcePath, Does.StartWith(Path.Combine(
                    Path.GetDirectoryName(savedSceneUri.LocalPath)!,
                    "resources",
                    "ai")));
                Assert.That(File.Exists(savedResourcePath), Is.True);
                Assert.That(File.Exists(resourcePath), Is.False);
                Assert.That(Directory.Exists(ownedDirectory), Is.False);
            }

            await TestShell.Editor.CloseTabItem(tab);
            Scene restored = CoreSerializer.RestoreFromUri<Scene>(savedSceneUri);
            Element restoredElement = restored.Children.Single();
            string restoredResource = restoredElement.Objects
                .OfType<Beutl.Graphics.SourceImage>()
                .Single()
                .Source.CurrentValue!.Uri.LocalPath;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restoredElement.Uri, Is.EqualTo(savedSidecar));
                Assert.That(File.Exists(restoredElement.Uri!.LocalPath), Is.True);
                Assert.That(restoredResource, Is.EqualTo(savedResourcePath));
                Assert.That(File.Exists(restoredResource), Is.True);
                Assert.That(Directory.Exists(ownedDirectory), Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(ownedDirectory))
                Directory.Delete(ownedDirectory, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task FirstSaveFailureRestoresUnsavedElementAndResourceUris()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-unsaved-save-rollback");
        Scene scene = editor.Scene;
        Uri eventualSceneUri = scene.Uri!;
        scene.Uri = null;
        string ownedDirectory = AiResultImporter.GetUnsavedSceneDirectory(scene.Id);
        string failureRoot = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            $"blocked-save-{Guid.NewGuid():N}");
        string directoryAtScenePath = Path.Combine(failureRoot, "Scene.scene");
        Directory.CreateDirectory(directoryAtScenePath);
        using var bitmap = new Bitmap(2, 2);
        var importer = new AiResultImporter(
            scene,
            editor.GetRequiredService<IElementAdder>());

        try
        {
            ElementAddResult result = await importer.ImportImageAsync(
                bitmap,
                new AiResultImportOptions(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    0,
                    "Unsaved AI image"));
            Assert.That(result.IsSuccess, Is.True, result.Failure?.Message);
            Element element = result.Elements.Single();
            Uri originalSidecar = element.Uri!;
            var source = element.Objects
                .OfType<Beutl.Graphics.SourceImage>()
                .Single()
                .Source.CurrentValue!;
            Uri originalResource = source.Uri;
            scene.Uri = new Uri(Path.GetFullPath(directoryAtScenePath));

            Assert.CatchAsync<Exception>(async () => await editor.Commands!.OnSave());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(element.Uri, Is.EqualTo(originalSidecar));
                Assert.That(source.Uri, Is.EqualTo(originalResource));
                Assert.That(File.Exists(originalSidecar.LocalPath), Is.True);
                Assert.That(File.Exists(originalResource.LocalPath), Is.True);
                Assert.That(Directory.GetFiles(failureRoot, "*.belm"), Is.Empty);
                Assert.That(
                    Directory.Exists(Path.Combine(failureRoot, "resources", "ai"))
                        ? Directory.GetFiles(Path.Combine(failureRoot, "resources", "ai"))
                        : [],
                    Is.Empty);
            }

            scene.Uri = eventualSceneUri;
            Assert.That(await editor.Commands!.OnSave(), Is.True);
        }
        finally
        {
            if (Directory.Exists(ownedDirectory))
                Directory.Delete(ownedDirectory, recursive: true);
            if (Directory.Exists(failureRoot))
                Directory.Delete(failureRoot, recursive: true);
        }
    }

    private sealed class CapturingElementAdder(int producedElementCount) : IElementAdder
    {
        public IElementSourceHandlerRegistry SourceHandlers { get; } = new ElementSourceHandlerRegistry();

        public string? StagedPath { get; private set; }

        public ValueTask<ElementAddResult> AddAsync(
            IReadOnlyList<ElementDescription> descriptions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ElementDescription description = descriptions.Single();
            StagedPath = ((ElementSource.File)description.Source).FileName;
            var result = new List<Element>(producedElementCount);
            for (int index = 0; index < producedElementCount; index++)
            {
                result.Add(new Element());
            }
            ElementAddResult addResult = result.Count == 0
                ? ElementAddResult.Failed(
                    new ElementMaterializationFailure("The test element could not be materialized."),
                    description)
                : ElementAddResult.Succeeded(
                [
                    new ElementAddItemResult(
                        description,
                        result[0],
                        result.Skip(1).ToArray()),
                ]);
            return ValueTask.FromResult(addResult);
        }
    }
}
