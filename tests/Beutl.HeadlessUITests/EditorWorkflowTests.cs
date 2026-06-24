using Avalonia.Headless.NUnit;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class EditorWorkflowTests
{
    private static void ResetProject() => TestReset.ResetShell();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await ProjectService.Current.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();

        EditorTabItem tab = EditorService.Current.SelectedTabItem.Value!;
        // The scene editor's IEditorContext is the EditViewModel itself.
        return (EditViewModel)tab.Context.Value;
    }

    private static Element AddRectangle(EditViewModel editor, TimeSpan start, int layer)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: start,
            Length: TimeSpan.FromSeconds(2),
            Layer: layer,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();
        return editor.Scene.Children[^1];
    }

    [AvaloniaTest]
    public async Task ActivateTabItem_exposes_the_EditViewModel_for_the_scene()
    {
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("editvm");

        Assert.That(editor, Is.Not.Null);
        Assert.That(editor.Scene, Is.SameAs(EditorService.Current.SelectedTabItem.Value!.Context.Value.Object));
        Assert.That(editor.HistoryManager, Is.Not.Null);
        Assert.That(editor.HistoryManager.Root, Is.SameAs(editor.Scene));
    }

    [AvaloniaTest]
    public async Task AddElement_through_the_editor_records_history_and_grows_the_scene()
    {
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("addelem");

        Assert.That(editor.Scene.Children, Is.Empty);
        Assert.That(editor.HistoryManager.CanUndo, Is.False);

        Element element = AddRectangle(editor, TimeSpan.Zero, layer: 0);

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
        Assert.That(editor.Scene.Children[0], Is.SameAs(element));
        Assert.That(element.Objects.OfType<RectShape>().Any(), Is.True);
        Assert.That(editor.HistoryManager.CanUndo, Is.True);
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task EditProperty_through_the_editor_records_a_second_history_entry()
    {
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("editprop");
        Element element = AddRectangle(editor, TimeSpan.Zero, layer: 0);
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(1));

        int originalZIndex = element.ZIndex;
        element.ZIndex = originalZIndex + 3;
        editor.HistoryManager.Commit("EditZIndex");
        HeadlessTestHelpers.Settle();

        Assert.That(element.ZIndex, Is.EqualTo(originalZIndex + 3));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task Undo_and_redo_restore_a_property_edit()
    {
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("undoprop");
        Element element = AddRectangle(editor, TimeSpan.Zero, layer: 0);

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
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("undoadd");
        Element element = AddRectangle(editor, TimeSpan.Zero, layer: 0);
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
    public async Task Undo_redo_through_known_editor_commands()
    {
        ResetProject();
        EditViewModel editor = await OpenEditorForNewScene("knowncmds");
        AddRectangle(editor, TimeSpan.Zero, layer: 0);
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));

        IKnownEditorCommands commands = editor.Commands!;
        bool undone = await commands.OnUndo();
        HeadlessTestHelpers.Settle();
        Assert.That(undone, Is.True);
        Assert.That(editor.Scene.Children, Is.Empty);

        bool redone = await commands.OnRedo();
        HeadlessTestHelpers.Settle();
        Assert.That(redone, Is.True);
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
    }
}
