using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Dock.Avalonia.Controls;

namespace Beutl.HeadlessUITests;

// Hosts the real scene-editor view (EditView from src/Beutl) bound to a real EditViewModel in a
// headless window. The full MainView is not hosted: it pulls app-only dock/title-bar resources and
// window chrome that the minimal TestApp does not register; EditView is the largest real shell view
// that inflates and lays out reliably headless.
[TestFixture]
public class ShellViewTests
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
        Project project = (await ProjectService.Current.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        EditorService.Current.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)EditorService.Current.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task EditView_inflates_and_lays_out_in_a_headless_window()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("editview");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            DockControl? dock = HeadlessTestHelpers.FindDescendant<DockControl>(view);
            Assert.That(dock, Is.Not.Null, "EditView should inflate its DockControl");
            Assert.That(view.IsAttachedToVisualTree(), Is.True);
            Assert.That(view.Bounds.Width, Is.GreaterThan(0));
            Assert.That(view.Bounds.Height, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task EditView_renders_to_a_headless_frame()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("editviewframe");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Size.Width, Is.GreaterThan(0));
            Assert.That(frame.Size.Height, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
