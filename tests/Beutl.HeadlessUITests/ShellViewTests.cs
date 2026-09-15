using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
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
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
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
    [TestCase(640, false)]
    [TestCase(1280, false)]
    [TestCase(640, true)]
    [TestCase(1280, true)]
    public async Task EditView_preserves_horizontal_dock_insets_when_resized(int width, bool light)
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene($"dock-insets-{width}-{light}");

        var view = new EditView { DataContext = editor };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 600,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };

        try
        {
            window.Show();
            DockControl dock = view.FindControl<DockControl>("DockControl")!;
            Assert.That(dock, Is.Not.Null);

            AssertInsets(width);
            int resizedWidth = width == 640 ? 1280 : 640;
            window.Width = resizedWidth;
            AssertInsets(resizedWidth);

            void AssertInsets(int expectedWidth)
            {
                HeadlessTestHelpers.Render();
                Point origin = dock.TranslatePoint(default, window)!.Value;

                // Child docks have separate minimum-width constraints; the root gutter must
                // remain fixed even when those constraints take effect in a narrow window.
                Assert.Multiple(() =>
                {
                    Assert.That(window.ClientSize.Width, Is.EqualTo(expectedWidth));
                    Assert.That(origin.X, Is.EqualTo(2), "left dock inset");
                    Assert.That(window.ClientSize.Width - origin.X - dock.Bounds.Width,
                        Is.EqualTo(2), "right dock inset");
                });
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // No frame-capture counterpart: GPU pixel readback of the fully-inflated shell crashes the test host
    // on software Vulkan (SwiftShader/MoltenVK). PreviewRenderTests covers GPU frame rendering instead.
}
