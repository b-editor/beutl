using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockLayoutTabTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            1920, 1080, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static DockLayoutPresetService NewService()
    {
        string path = Path.Combine(
            BeutlHomeIsolation.CurrentHome!, $"dock-layout-presets-{Guid.NewGuid():N}.json");
        return new DockLayoutPresetService(path);
    }

    private static string[] ToolExtensionNames(EditViewModel editor)
    {
        return editor.DockHost.Factory.EnumerateTools()
            .Select(t => t.ToolContext.Extension.GetType().FullName!)
            .OrderBy(n => n)
            .ToArray();
    }

    [AvaloniaTest]
    public async Task Tab_saves_the_current_arrangement_and_applies_it_back()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-save-apply");

        var viewModel = new DockLayoutViewModel(editor, NewService());

        Assert.That(viewModel.Save("Default"), Is.Not.Null);
        string[] saved = ToolExtensionNames(editor);

        // Saving selects the new entry so the row actions act on it.
        Assert.That(viewModel.SelectedItem.Value?.Name.Value, Is.EqualTo("Default"));
        Assert.That(viewModel.HasSelection.Value, Is.True);

        BeutlToolDockable library = editor.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);
        editor.DockHost.CloseToolTab(library.ToolContext);
        HeadlessTestHelpers.Settle();
        Assert.That(ToolExtensionNames(editor), Is.Not.EqualTo(saved));

        Assert.That(viewModel.Apply(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(ToolExtensionNames(editor), Is.EqualTo(saved));

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Saving_over_an_existing_name_replaces_it_and_blank_names_are_rejected()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-overwrite");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);

        viewModel.Save("Editing");

        // Case-insensitive: this overwrites rather than adding a second entry.
        viewModel.Save("editing");
        Assert.That(service.Items, Has.Count.EqualTo(1), "an overwrite must not add a second entry");

        Assert.That(viewModel.Save("   "), Is.Null);
        Assert.That(viewModel.Save(null), Is.Null);
        Assert.That(service.Items, Has.Count.EqualTo(1));

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Suggested_name_avoids_colliding_with_a_saved_layout()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-suggest");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);

        string first = viewModel.SuggestName();
        Assert.That(first, Is.EqualTo(Strings.DockLayout));

        viewModel.Save(first);
        Assert.That(viewModel.SuggestName(), Is.EqualTo($"{Strings.DockLayout} 2"));

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Tab_renames_and_removes_a_saved_layout()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-manage");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);

        viewModel.Save("Editing");
        viewModel.Save("Grading");

        DockLayoutPresetItem editing = service.Items[0];

        // A name already taken by another layout is refused; an unchanged one is a no-op.
        Assert.That(viewModel.Rename(editing, "Grading"), Is.False);
        Assert.That(viewModel.Rename(editing, "  "), Is.False);
        Assert.That(viewModel.Rename(editing, "Editing"), Is.True);
        Assert.That(service.Items[0].Name.Value, Is.EqualTo("Editing"));

        Assert.That(viewModel.Rename(editing, "Rough cut"), Is.True);
        Assert.That(service.Items[0].Name.Value, Is.EqualTo("Rough cut"));

        // Re-saving under an existing name is how a layout gets refreshed.
        BeutlToolDockable library = editor.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);
        editor.DockHost.CloseToolTab(library.ToolContext);
        HeadlessTestHelpers.Settle();

        Assert.That(viewModel.Save("Rough cut"), Is.Not.Null);
        Assert.That(service.Items, Has.Count.EqualTo(2));
        Assert.That(viewModel.SelectedItem.Value?.Name.Value, Is.EqualTo("Rough cut"));

        string[] afterClose = ToolExtensionNames(editor);
        editor.DockHost.ResetLayout();
        HeadlessTestHelpers.Settle();
        Assert.That(viewModel.Apply(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(ToolExtensionNames(editor), Is.EqualTo(afterClose));

        viewModel.Remove(service.Items[0]);
        Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Grading" }));
        Assert.That(viewModel.HasSelection.Value, Is.False, "removing the selected layout clears the selection");

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Tab_extension_creates_its_view_and_context()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-extension");

        Assert.That(
            DockLayoutTabExtension.Instance.TryCreateContext(editor, out IToolContext? context), Is.True);
        Assert.That(context, Is.InstanceOf<DockLayoutViewModel>());
        Assert.That(context!.Extension, Is.SameAs(DockLayoutTabExtension.Instance));
        Assert.That(context.Header, Is.EqualTo(Strings.DockLayout));

        Assert.That(DockLayoutTabExtension.Instance.TryCreateContent(editor, out Control? control), Is.True);
        Assert.That(control, Is.InstanceOf<DockLayoutView>());

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);
        control!.DataContext = viewModel;

        ListBox list = control.GetLogicalDescendants().OfType<ListBox>().Single();
        Assert.That(list.ItemsSource, Is.SameAs(service.Items));

        context.Dispose();
        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Row_actions_target_the_row_they_belong_to()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-row-actions");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);
        viewModel.Save("Editing");
        viewModel.Save("Grading");

        // Row actions must follow their own row, not the selection.
        viewModel.SelectedItem.Value = service.Items[1];

        Assert.That(viewModel.Rename(service.Items[0], "Rough cut"), Is.True);
        Assert.That(service.Items[0].Name.Value, Is.EqualTo("Rough cut"));
        Assert.That(viewModel.SelectedItem.Value?.Name.Value, Is.EqualTo("Grading"));

        viewModel.Remove(service.Items[0]);
        Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Grading" }));
        Assert.That(
            viewModel.HasSelection.Value, Is.True,
            "removing another row must not clear the selection");

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task Apply_button_applies_the_row_it_belongs_to()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-apply-row");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);

        viewModel.Save("Full");
        string[] full = ToolExtensionNames(editor);

        BeutlToolDockable library = editor.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);
        editor.DockHost.CloseToolTab(library.ToolContext);
        HeadlessTestHelpers.Settle();

        viewModel.Save("No library");
        string[] reduced = ToolExtensionNames(editor);
        Assert.That(reduced, Is.Not.EqualTo(full));

        // Saving selected "No library", so applying the other row proves the button follows it.
        Assert.That(viewModel.SelectedItem.Value?.Name.Value, Is.EqualTo("No library"));

        DockLayoutPresetItem fullItem = service.Items.First(i => i.Name.Value == "Full");
        Assert.That(viewModel.Apply(fullItem), Is.True);
        HeadlessTestHelpers.Settle();

        Assert.That(ToolExtensionNames(editor), Is.EqualTo(full));

        viewModel.Dispose();
    }

    [AvaloniaTest]
    public async Task A_failed_remove_keeps_the_layout_and_the_selection()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-remove-failure");

        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"presets-{Guid.NewGuid():N}.json");
        var service = new DockLayoutPresetService(path);
        var viewModel = new DockLayoutViewModel(editor, service);
        viewModel.Save("Editing");

        // Replacing the store file with a directory makes the next write throw.
        File.Delete(path);
        Directory.CreateDirectory(path);

        try
        {
            DockLayoutPresetItem target = service.Items[0];
            viewModel.SelectedItem.Value = target;

            viewModel.Remove(target);

            // The service rolled the delete back, so clearing the selection would leave the tab
            // claiming nothing is selected while the row is still listed.
            Assert.That(service.Items, Has.Count.EqualTo(1));
            Assert.That(viewModel.SelectedItem.Value, Is.SameAs(target));
            Assert.That(viewModel.HasSelection.Value, Is.True);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Revealing_the_row_actions_does_not_change_the_row_height()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-row-height");

        DockLayoutPresetService service = NewService();
        var viewModel = new DockLayoutViewModel(editor, service);
        viewModel.Save("Editing");
        viewModel.Save("Color grading");
        viewModel.Save("Audio mixing");
        viewModel.SelectedItem.Value = null;

        var view = new DockLayoutView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 300, Height = 420 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ListBoxItem[] rows = view.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.That(rows, Has.Length.EqualTo(3));

            double[] resting = rows.Select(r => r.Bounds.Height).ToArray();
            Assert.That(resting, Has.All.GreaterThan(0));

            // Selection uses the same style trigger as hovering, without synthetic pointer input.
            viewModel.SelectedItem.Value = service.Items[1];
            HeadlessTestHelpers.Render();

            Assert.That(
                rows.Select(r => r.Bounds.Height).ToArray(), Is.EqualTo(resting),
                "rows must not grow when their actions become visible");

            foreach (ListBoxItem row in rows)
            {
                foreach (Button button in row.GetVisualDescendants().OfType<Button>())
                {
                    Assert.That(
                        VerticalCenterIn(button, row),
                        Is.EqualTo(row.Bounds.Height / 2).Within(1.0),
                        "a row action must be vertically centered in its row");
                }
            }
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    // Vertical center of `child` in `ancestor` coordinates.
    private static double VerticalCenterIn(Visual child, Visual ancestor)
    {
        double y = child.Bounds.Height / 2;
        Visual? visual = child;
        while (visual is not null && !ReferenceEquals(visual, ancestor))
        {
            y += visual.Bounds.Y;
            visual = visual.GetVisualParent();
        }

        return y;
    }

    [AvaloniaTest]
    public async Task Tab_is_registered_and_opens_from_the_extension_provider()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tab-registered");

        Assert.That(
            editor.ExtensionProvider.AllExtensions.OfType<ToolTabExtension>(),
            Does.Contain(DockLayoutTabExtension.Instance),
            "the tab must be registered so it shows up in the tool tab menu");

        Assert.That(editor.DockHost.FindToolContext(typeof(DockLayoutTabExtension)), Is.Null);

        Assert.That(
            DockLayoutTabExtension.Instance.TryCreateContext(editor, out IToolContext? context), Is.True);
        Assert.That(editor.OpenToolTab(context!), Is.True);
        HeadlessTestHelpers.Settle();

        Assert.That(editor.DockHost.FindToolContext(typeof(DockLayoutTabExtension)), Is.SameAs(context));
    }
}
