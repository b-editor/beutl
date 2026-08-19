using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Beutl.Views.Dock;
using Dock.Model.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class FileBrowserMultipleTabsTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static BeutlToolDockable[] FileBrowsers(EditViewModel editor)
    {
        return editor.DockHost.Factory.EnumerateTools()
            .Where(t => t.ToolContext.Extension is FileBrowserTabExtension)
            .ToArray();
    }

    private static string NewDirectory(EditViewModel editor, string name)
    {
        string path = Path.Combine(
            Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [AvaloniaTest]
    public async Task A_second_file_browser_tab_can_be_opened()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-second-tab");
        IToolDock left = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;

        Assert.That(editor.DockHost.OpenToolTabFromExtension(FileBrowserTabExtension.Instance, left), Is.True);
        HeadlessTestHelpers.Settle();

        BeutlToolDockable[] browsers = FileBrowsers(editor);
        Assert.Multiple(() =>
        {
            Assert.That(browsers, Has.Length.EqualTo(2));
            Assert.That(browsers.Select(b => b.Id), Is.Unique);
        });
    }

    [AvaloniaTest]
    public async Task Each_file_browser_tab_is_titled_after_the_folder_it_shows()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-titles");
        IToolDock left = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
        editor.DockHost.OpenToolTabFromExtension(FileBrowserTabExtension.Instance, left);
        HeadlessTestHelpers.Settle();

        BeutlToolDockable[] browsers = FileBrowsers(editor);
        string first = NewDirectory(editor, "resources");
        string second = NewDirectory(editor, "captures");

        Assert.That(browsers[0].Title, Is.EqualTo(Strings.FileBrowser));

        ((FileBrowserTabViewModel)browsers[0].ToolContext).RootPath.Value = first;
        ((FileBrowserTabViewModel)browsers[1].ToolContext).RootPath.Value = second;
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(browsers[0].Title, Is.EqualTo("resources"));
            Assert.That(browsers[1].Title, Is.EqualTo("captures"));
        });

        ((FileBrowserTabViewModel)browsers[0].ToolContext).NavigateToHome();
        HeadlessTestHelpers.Settle();

        Assert.That(browsers[0].Title, Is.EqualTo(Strings.FileBrowser));
    }

    [AvaloniaTest]
    public async Task Two_file_browser_tabs_survive_a_view_state_round_trip()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-roundtrip");
        IToolDock left = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
        editor.DockHost.OpenToolTabFromExtension(FileBrowserTabExtension.Instance, left);
        HeadlessTestHelpers.Settle();

        BeutlToolDockable[] browsers = FileBrowsers(editor);
        string first = NewDirectory(editor, "resources");
        string second = NewDirectory(editor, "captures");
        ((FileBrowserTabViewModel)browsers[0].ToolContext).RootPath.Value = first;
        ((FileBrowserTabViewModel)browsers[1].ToolContext).RootPath.Value = second;
        HeadlessTestHelpers.Settle();

        var json = new JsonObject();
        editor.DockHost.WriteToJson(json);

        var restored = new DockHostViewModel("filebrowser-roundtrip", editor);
        try
        {
            restored.ReadFromJson(json);
            HeadlessTestHelpers.Settle();

            string[] paths = restored.Factory.EnumerateTools()
                .Select(t => t.ToolContext)
                .OfType<FileBrowserTabViewModel>()
                .Select(vm => vm.RootPath.Value)
                .ToArray();

            Assert.That(paths, Is.EquivalentTo(new[] { first, second }));
        }
        finally
        {
            restored.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task A_legacy_layout_restores_a_single_file_browser_and_keeps_its_id()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-legacy-id");

        string folder = NewDirectory(editor, "resources");
        FileBrowserTabViewModel browser =
            (FileBrowserTabViewModel)FileBrowsers(editor).Single().ToolContext;
        browser.RootPath.Value = folder;
        HeadlessTestHelpers.Settle();

        var json = new JsonObject();
        editor.DockHost.WriteToJson(json);
        string legacyId = typeof(FileBrowserTabExtension).FullName!;
        RewriteFileBrowserId(json, legacyId);

        var restored = new DockHostViewModel("filebrowser-legacy-id", editor);
        try
        {
            restored.ReadFromJson(json);
            HeadlessTestHelpers.Settle();

            BeutlToolDockable[] browsers = restored.Factory.EnumerateTools()
                .Where(t => t.ToolContext.Extension is FileBrowserTabExtension)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(browsers, Has.Length.EqualTo(1));
                Assert.That(browsers[0].Id, Is.EqualTo(legacyId));
                Assert.That(
                    ((FileBrowserTabViewModel)browsers[0].ToolContext).RootPath.Value,
                    Is.EqualTo(folder));
                Assert.That(browsers[0].Title, Is.EqualTo("resources"));
            });
        }
        finally
        {
            restored.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task The_add_menu_keeps_the_file_browser_enabled_once_it_is_open()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-add-menu");
        IToolDock left = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;

        var button = new ToolTabAddButton { DataContext = left };
        ContextMenu menu = button.CreateContextMenu()!;
        MenuItem item = menu.ItemsSource!
            .Cast<MenuItem>()
            .Single(i => ReferenceEquals(i.DataContext, FileBrowserTabExtension.Instance));

        Assert.That(item.IsEnabled, Is.True);
    }

    [AvaloniaTest]
    public async Task Closing_one_file_browser_tab_leaves_the_other_usable()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("filebrowser-close-one");
        IToolDock left = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)!;
        editor.DockHost.OpenToolTabFromExtension(FileBrowserTabExtension.Instance, left);
        HeadlessTestHelpers.Settle();

        BeutlToolDockable[] browsers = FileBrowsers(editor);
        var survivor = (FileBrowserTabViewModel)browsers[1].ToolContext;

        editor.DockHost.CloseToolTab(browsers[0].ToolContext);
        HeadlessTestHelpers.Settle();

        survivor.NavigateToHome();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(FileBrowsers(editor), Has.Length.EqualTo(1));
            // Fixed folders should survive closing the sibling tab.
            Assert.That(survivor.FavoriteItems, Is.Not.Empty);
        });
    }

    private static void RewriteFileBrowserId(JsonNode? node, string id)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$type"]?.GetValue<string>() == "tool"
                    && obj["extension"] is JsonObject ext
                    && ext["$type"]?.GetValue<string>() is { } discriminator
                    && discriminator.Contains(nameof(FileBrowserTabExtension), StringComparison.Ordinal))
                {
                    obj["id"] = id;
                }

                foreach (var pair in obj.ToArray())
                {
                    RewriteFileBrowserId(pair.Value, id);
                }

                break;

            case JsonArray array:
                foreach (JsonNode? item in array.ToArray())
                {
                    RewriteFileBrowserId(item, id);
                }

                break;
        }
    }
}
