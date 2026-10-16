using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class EditorSelectionTests
{
    [AvaloniaTest]
    public async Task The_timeline_shows_the_editor_selection_which_another_object_replaces()
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenAsync("editor-selection", 3);
        IEditorSelection selection = editor.GetRequiredService<IEditorSelection>();
        ElementViewModel[] items = [.. timeline.Elements.OrderBy(item => item.Model.ZIndex)];

        timeline.SelectElement(items[0]);
        timeline.SelectElement(items[1]);
        Assert.That(selection.SelectedElements, Is.EqualTo(new[] { items[0].Model, items[1].Model }));
        using (var reopened = new TimelineTabViewModel(editor))
            Assert.That(reopened.SelectedElements.Select(item => item.Model), Is.EquivalentTo(selection.SelectedElements));

        selection.SelectedObject.Value = items[1].Model;
        Assert.That(selection.SelectedElements, Has.Count.EqualTo(2), "An object inside the selection keeps it.");
        selection.SelectedObject.Value = items[2].Model;
        Assert.Multiple(() =>
        {
            Assert.That(selection.SelectedElements, Is.EqualTo(new[] { items[2].Model }));
            Assert.That(items.Select(item => item.IsSelected.Value), Is.EqualTo(new[] { false, false, true }));
        });

        editor.GetRequiredService<IElementStructureService>().Delete(editor.Scene, [items[2].Model]);
        Assert.That(selection.SelectedElements, Is.Empty);
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task Delete_removes_the_selection_and_the_groups_of_every_selected_element(bool fromTimeline)
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenAsync("editor-selection-delete", 5);
        Element[] elements = [.. editor.Scene.Children.OrderBy(element => element.ZIndex)];
        editor.Scene.Groups.Add([elements[0].Id, elements[1].Id]);
        editor.Scene.Groups.Add([elements[2].Id, elements[3].Id]);
        editor.GetRequiredService<IEditorSelection>().SelectedElements.AddRange([elements[0], elements[2]]);

        if (fromTimeline)
        {
            await timeline.ExecuteAsync(new ContextCommandExecution("Delete"));
        }
        else
        {
            // The menu asks for no confirmation and needs no timeline.
            editor.CloseToolTab(timeline);
            HeadlessTestHelpers.Settle();
            await TestShell.MainViewModel.MenuBar.DeleteLayer.ExecuteAsync();
        }

        Assert.That(editor.Scene.Children, Is.EqualTo(new[] { elements[4] }));
    }

    private static Task<(EditViewModel Editor, TimelineTabViewModel Timeline)> OpenAsync(string name, int count)
        => TimelineTestHost.OpenTimelineAsync(name, [.. Enumerable.Range(0, count).Select(layer => new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(1), layer, new ElementSource.EngineObject(() => new RectShape())))]);
}
