using System.Buffers.Binary;
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
using Beutl.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiResultImporterTests
{
    [AvaloniaTest]
    public async Task First_save_keeps_a_shared_unsaved_resource_for_an_undo_owned_source()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("undo-owned-unsaved-resource");
        Scene scene = editor.Scene;
        Uri savedUri = scene.Uri!;
        scene.Uri = null;
        using var bitmap = new Bitmap(2, 2);
        var importer = new AiResultImporter(scene, editor.GetRequiredService<IElementAdder>());
        ElementAddResult added = await importer.ImportImageAsync(bitmap,
            new AiResultImportOptions(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, "Shared resource"));
        Assert.That(added.IsSuccess, Is.True);
        Element element = added.Elements.Single();
        Uri original = element.Objects.OfType<Beutl.Graphics.SourceImage>().Single().Source.CurrentValue!.Uri;
        var historySource = new Beutl.Media.Source.ImageSource();
        historySource.ReadFrom(original);
        var historyObject = new Beutl.Graphics.SourceImage();
        historyObject.Source.CurrentValue = historySource;
        element.AddObject(historyObject);
        editor.HistoryManager.Commit("Add history source");
        element.Objects.Remove(historyObject);
        editor.HistoryManager.Commit("Remove history source");
        scene.Uri = savedUri;
        Assert.That(await editor.Commands!.OnSave(), Is.True);
        Assert.That(editor.HistoryManager.Undo(), Is.True);
        Assert.That(element.Objects, Does.Contain(historyObject));
        Assert.That(historySource.Uri, Is.EqualTo(original));
        Assert.That(File.Exists(historySource.Uri.LocalPath), Is.True);
        await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        Assert.That(File.Exists(original.LocalPath), Is.False);
    }

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
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
    public async Task ImportImage_EncodeFailureDoesNotPublishResourceOrElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-result-importer-encode-failure");
        using var bitmap = new Bitmap(new SKBitmap());
        var adder = new CapturingElementAdder(producedElementCount: 1);
        var importer = new AiResultImporter(editor.Scene, adder);
        string resourceDirectory = Path.Combine(
            Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
            "resources",
            "ai");

        Assert.ThrowsAsync<IOException>(() => importer.ImportImageAsync(
            bitmap,
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                0,
                "AI image")));

        Assert.Multiple(() =>
        {
            Assert.That(adder.StagedPath, Is.Null);
            Assert.That(Directory.EnumerateFiles(resourceDirectory), Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task ImportImageBytes_RejectsOversizedDimensionsBeforeBitmapDecode()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-result-importer-bomb");
        var importer = new AiResultImporter(
            editor.Scene,
            editor.GetRequiredService<IElementAdder>());
        byte[] oversized = PngWithDimensions(8_193, 1);

        Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportImageAsync(
            oversized,
            new AiResultImportOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                0,
                "AI image")));
        Assert.That(editor.Scene.Children, Is.Empty);
    }

    [Test]
    public void OutpaintDimensions_RejectExpandedPixelBoundsBeforeMakeBorder()
    {
        Assert.That(
            AiImageEditDialogViewModel.GetOutpaintDimensions(1_365, 1_365, 100),
            Is.EqualTo((4_095, 4_095, 1_365, 1_365)));
        Assert.Throws<InvalidDataException>(() =>
            AiImageEditDialogViewModel.GetOutpaintDimensions(1_366, 1_366, 100));
    }

    private static byte[] PngWithDimensions(int width, int height)
    {
        using var source = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKImage image = SKImage.FromBitmap(source);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] bytes = encoded.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), Crc32(bytes.AsSpan(12, 17)));
        return bytes;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320 ^ (crc >> 1);
        }
        return ~crc;
    }

    [AvaloniaTest]
    public async Task ImportVideoBytes_StagesAndImportsEveryProducedElement()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-video-importer");
        var adder = new CapturingElementAdder(producedElementCount: 2);
        var importer = new AiResultImporter(editor.Scene, adder, AcceptVideoAsync);

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
        var importer = new AiResultImporter(editor.Scene, adder, AcceptVideoAsync);
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
        var importer = new AiResultImporter(editor.Scene, adder, AcceptVideoAsync);

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
        var importer = new AiResultImporter(scene, adder, AcceptVideoAsync);
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
        var importer = new AiResultImporter(scene, adder, AcceptVideoAsync);
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
    public async Task ImportVideo_InvalidContainerIsRejectedBeforeElementMaterialization()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-invalid-ai-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var scene = new Scene(640, 480, "invalid-video")
        {
            Uri = new Uri(Path.Combine(directory, "scene.scene")),
        };
        var adder = new CapturingElementAdder(producedElementCount: 1);
        var importer = new AiResultImporter(scene, adder);
        string resourceDirectory = Path.Combine(directory, "resources", "ai");
        try
        {
            try
            {
                await importer.ImportVideoAsync(
                    new byte[] { 1, 2, 3, 4 },
                    new AiResultImportOptions(
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(4),
                        0,
                        "AI video"));
                Assert.Fail("A truncated AI video must be rejected before materialization.");
            }
            catch (InvalidDataException)
            {
            }

            Assert.Multiple(() =>
            {
                Assert.That(adder.StagedPath, Is.Null);
                Assert.That(
                    Directory.Exists(resourceDirectory)
                        ? Directory.EnumerateFiles(resourceDirectory)
                        : [],
                    Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(".mp4")]
    [TestCase(".mov")]
    public void VideoContainerSignature_AcceptsLegalBoxesBeforeFileType(string extension)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"beutl-leading-boxes-{Guid.NewGuid():N}{extension}");
        var bytes = new byte[56];
        WriteBoxHeader(bytes.AsSpan(0, 8), 8, "free"u8);
        WriteBoxHeader(bytes.AsSpan(8, 32), 1, "uuid"u8);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16, 8), 32);
        WriteBoxHeader(bytes.AsSpan(40, 16), 16, "ftyp"u8);
        "isom"u8.CopyTo(bytes.AsSpan(48, 4));
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.DoesNotThrow(() => AiResultImporter.ValidateVideoContainerSignature(path));
        }
        finally
        {
            File.Delete(path);
        }

        static void WriteBoxHeader(
            Span<byte> destination,
            uint size,
            ReadOnlySpan<byte> type)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[..4], size);
            type.CopyTo(destination[4..8]);
        }
    }

    [Test]
    public void VideoContainerSignature_RejectsMalformedNormalAndExtendedBoxes()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"beutl-malformed-box-{Guid.NewGuid():N}.mp4");
        (string Name, byte[] Bytes)[] cases =
        [
            ("truncated normal header", new byte[7]),
            ("undersized normal box", BoxHeader(7)),
            ("oversized normal box", BoxHeader(9)),
            ("truncated extended header", BoxHeader(1)),
            ("undersized extended box", ExtendedBoxHeader(15)),
            ("oversized extended box", ExtendedBoxHeader(17)),
        ];
        try
        {
            foreach ((string name, byte[] bytes) in cases)
            {
                File.WriteAllBytes(path, bytes);
                Assert.Throws<InvalidDataException>(
                    () => AiResultImporter.ValidateVideoContainerSignature(path),
                    name);
            }
        }
        finally
        {
            File.Delete(path);
        }

        static byte[] BoxHeader(uint size)
        {
            var result = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), size);
            "free"u8.CopyTo(result.AsSpan(4, 4));
            return result;
        }

        static byte[] ExtendedBoxHeader(ulong size)
        {
            var result = BoxHeader(1);
            Array.Resize(ref result, 16);
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8, 8), size);
            return result;
        }
    }

    private static Task AcceptVideoAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.That(File.Exists(path), Is.True);
        return Task.CompletedTask;
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
                Assert.That(File.Exists(resourcePath), Is.True,
                    "Distinct sources retained by undo history may still use the original URI.");
                Assert.That(Directory.Exists(ownedDirectory), Is.True);
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
