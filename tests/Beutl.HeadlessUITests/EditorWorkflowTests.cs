using System.Runtime.Versioning;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using SkiaSharp;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class EditorWorkflowTests
{
    private static async Task<Element> AddRectangle(EditViewModel editor, MediaTime start, int layer)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(
            Start: start,
            Length: MediaTime.FromSeconds(2),
            Layer: layer,
            Source: new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        return editor.Scene.Children[^1];
    }

    [AvaloniaTest]
    public async Task ActivateTabItem_exposes_the_EditViewModel_for_the_scene()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("editvm");

        Assert.That(editor, Is.Not.Null);
        Assert.That(editor.Scene, Is.SameAs(TestShell.Editor.SelectedTabItem.Value!.Context.Value.Object));
        Assert.That(editor.HistoryManager, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task AddElement_through_the_editor_records_history_and_grows_the_scene()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("addelem");

        Assert.That(editor.Scene.Children, Is.Empty);
        Assert.That(editor.HistoryManager.CanUndo, Is.False);

        Element element = await AddRectangle(editor, MediaTime.Zero, layer: 0);

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
        Assert.That(editor.Scene.Children[0], Is.SameAs(element));
        Assert.That(element.Objects.OfType<RectShape>().Any(), Is.True);
        Assert.That(editor.HistoryManager.CanUndo, Is.True);
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task AddElement_uses_temporary_storage_when_the_scene_is_unsaved()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("addelem-unsaved");
        Uri savedUri = editor.Scene.Uri!;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;

        try
        {
            editor.Scene.Uri = null;

            ElementAddResult result = await adder.AddAsync([new ElementDescription(
                Start: MediaTime.Zero,
                Length: MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape()))],
                CancellationToken.None);
            HeadlessTestHelpers.Settle();

            Element element = result.Elements.Single();
            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
                Assert.That(editor.HistoryManager.CanUndo, Is.True);
                Assert.That(
                    FilePathComparison.IsSameOrDescendant(
                        UnsavedSceneStorage.GetDirectory(editor.Scene.Id),
                        element.Uri!.LocalPath),
                    Is.True);
                Assert.That(File.Exists(element.Uri.LocalPath), Is.True);
            });
        }
        finally
        {
            editor.Scene.Uri = savedUri;
            UnsavedSceneStorage.Cleanup(editor.Scene.Id);
        }
    }

    [AvaloniaTest]
    [UnsupportedOSPlatform("windows")]
    public async Task Save_succeeds_for_a_source_beneath_a_traverse_only_ancestor()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Unix directory permissions are required.");
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("save-traverse-only");
        string locked = Path.Combine(TestShell.NewWorkspace("save-traverse-only-media"), "locked");
        string imagePath = Path.Combine(locked, "media", "frame.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        File.WriteAllBytes(imagePath, TinyPng());
        UnixFileMode originalMode = File.GetUnixFileMode(locked);
        try
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserExecute);
            try
            {
                Directory.GetFileSystemEntries(locked);
                Assert.Ignore("Directory listing permissions are not enforced here.");
            }
            catch (UnauthorizedAccessException)
            {
                // The ancestor can be traversed but not listed, which is the shape under test.
            }

            var source = new ImageSource();
            source.ReadFrom(new Uri(imagePath));
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            await adder.AddAsync([new ElementDescription(
                Start: MediaTime.Zero,
                Length: MediaTime.FromSeconds(2),
                Layer: 0,
                Source: new ElementSource.EngineObject(() =>
                {
                    var drawable = new SourceImage();
                    drawable.Source.Value = source;
                    return drawable;
                }))],
                CancellationToken.None);
            HeadlessTestHelpers.Settle();
            Element element = editor.Scene.Children[^1];

            Assert.That(await editor.SaveAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(editor.Scene.Uri!.LocalPath), Is.True);
                Assert.That(File.Exists(element.Uri!.LocalPath), Is.True);
                // External media is referenced in place; only owned temporary resources relocate.
                Assert.That(source.Uri.LocalPath, Is.EqualTo(imagePath));
            });
        }
        finally
        {
            File.SetUnixFileMode(locked, originalMode);
        }
    }

    private static byte[] TinyPng()
    {
        using var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [AvaloniaTest]
    public async Task Undo_and_redo_restore_a_property_edit()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("undoprop");
        Element element = await AddRectangle(editor, MediaTime.Zero, layer: 0);

        // Exercises the observer-to-history pipeline, not an edit service.
        int originalZIndex = element.ZIndex;
        element.ZIndex = originalZIndex + 5;
        editor.HistoryManager.Commit("EditZIndex");
        HeadlessTestHelpers.Settle();
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(2));

        bool undone = editor.HistoryManager.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(undone, Is.True);
        Assert.That(element.ZIndex, Is.EqualTo(originalZIndex));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(1));

        bool redone = editor.HistoryManager.Redo();
        HeadlessTestHelpers.Settle();
        Assert.That(redone, Is.True);
        Assert.That(element.ZIndex, Is.EqualTo(originalZIndex + 5));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task Undo_removes_the_added_element_and_redo_restores_it()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("undoadd");
        Element element = await AddRectangle(editor, MediaTime.Zero, layer: 0);
        Guid elementId = element.Id;
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));

        bool undone = editor.HistoryManager.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(undone, Is.True);
        Assert.That(editor.Scene.Children, Is.Empty);
        Assert.That(editor.HistoryManager.CanUndo, Is.False);

        bool redone = editor.HistoryManager.Redo();
        HeadlessTestHelpers.Settle();
        Assert.That(redone, Is.True);
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
        Assert.That(editor.Scene.Children[0].Id, Is.EqualTo(elementId));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Undo_redo_through_editor_history_capability(bool useMenu)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(useMenu ? "editor-history-menu" : "editor-history");
        await AddRectangle(editor, MediaTime.Zero, layer: 0);
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));

        IUndoRedoEditorContext history = editor;
        if (useMenu)
            await TestShell.MainViewModel.MenuBar.Undo.ExecuteAsync();
        else
            Assert.That(await history.UndoAsync(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(editor.Scene.Children, Is.Empty);

        if (useMenu)
            await TestShell.MainViewModel.MenuBar.Redo.ExecuteAsync();
        else
            Assert.That(await history.RedoAsync(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
    }
}
