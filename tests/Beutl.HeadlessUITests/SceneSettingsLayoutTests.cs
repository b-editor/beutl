using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab.Views;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SceneSettingsLayoutTests
{
    [AvaloniaTest]
    [TestCase(320, 160, false)]
    [TestCase(320, 160, true)]
    [TestCase(640, 420, false)]
    [TestCase(640, 420, true)]
    public async Task Inputs_and_actions_remain_reachable_in_short_docks(int width, int height, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"scene-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "scene-settings", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var options = (ITimelineOptionsProvider)editor.GetService(typeof(ITimelineOptionsProvider))!;
        using var model = new SceneSettingsTabViewModel(editor);
        var view = new SceneSettingsTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = height,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ScrollViewer scroll = view.FindControl<ScrollViewer>("SettingsScrollViewer")!;
            Button apply = view.FindControl<Button>("ApplyButton")!;
            Button revert = view.FindControl<Button>("RevertButton")!;
            TextBox start = view.FindControl<TextBox>("StartInputTextBox")!;
            TextBox duration = view.FindControl<TextBox>("DurationInputTextBox")!;
            IntEditor layers = view.FindControl<IntEditor>("countEditor")!;
            Capture(window, $"initial-{width}x{height}-{light}");

            if (height == 160)
            {
                Assert.That(scroll.Extent.Height, Is.GreaterThan(scroll.Viewport.Height));
                window.MouseWheel(new Point(2, height / 2d), new Vector(0, -20));
                HeadlessTestHelpers.Render();
                Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
            }
            AssertInViewport(apply, scroll);
            AssertInViewport(revert, scroll);
            Capture(window, $"actions-{width}x{height}-{light}");

            // Keyboard focus brings a field back into view from the bottom of the form.
            start.Focus(NavigationMethod.Tab);
            HeadlessTestHelpers.Render();
            AssertInViewport(start, scroll);
            start.Text = "invalid";
            HeadlessTestHelpers.Render();
            Assert.That(apply.IsEffectivelyEnabled, Is.False);

            TimeSpan originalStart = scene.Start;
            start.Text = "00:00:02";
            duration.Text = "00:00:12";
            layers.Value = 60;
            HeadlessTestHelpers.Render();
            Assert.That(scene.Start, Is.EqualTo(originalStart), "Edits remain drafts until Apply is clicked.");
            Assert.That(apply.IsEffectivelyEnabled, Is.True);
            FocusAndClick(apply);
            for (int attempt = 0; attempt < 500 && options.Options.Value.MaxLayerCount != 60; attempt++)
            {
                await Task.Delay(10);
                HeadlessTestHelpers.Settle();
            }
            Assert.Multiple(() =>
            {
                Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(12)));
                Assert.That(options.Options.Value.MaxLayerCount, Is.EqualTo(60));
            });

            start.Text = "00:00:03";
            duration.Text = "00:00:15";
            layers.Value = 70;
            FocusAndClick(revert);
            Assert.Multiple(() =>
            {
                Assert.That(start.Text, Is.EqualTo("00:00:02"));
                Assert.That(duration.Text, Is.EqualTo("00:00:12"));
                Assert.That(layers.Value, Is.EqualTo(60));
            });

            void FocusAndClick(Button button)
            {
                button.Focus(NavigationMethod.Tab);
                HeadlessTestHelpers.Render();
                AssertInViewport(button, scroll);
                Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Long_action_labels_wrap_without_horizontal_clipping(bool light)
    {
        using var command = new ReactiveCommand();
        var view = new SceneSettingsTabView();
        var window = new Window
        {
            Content = view,
            Width = 220,
            Height = 160,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            Button apply = view.FindControl<Button>("ApplyButton")!;
            Button revert = view.FindControl<Button>("RevertButton")!;
            apply.Command = command;
            revert.Command = command;
            ((TextBlock)apply.Content!).Text = "Apply scene settings";
            ((TextBlock)revert.Content!).Text = "Discard all pending changes to the scene settings";
            window.Show();
            HeadlessTestHelpers.Render();
            ScrollViewer scroll = view.FindControl<ScrollViewer>("SettingsScrollViewer")!;
            Assert.That(revert.Bounds.Top, Is.GreaterThanOrEqualTo(apply.Bounds.Bottom));
            Assert.That(revert.Bounds.Height, Is.GreaterThan(apply.Bounds.Height));
            foreach (Button button in new[] { apply, revert })
            {
                button.Focus(NavigationMethod.Tab);
                HeadlessTestHelpers.Render();
                AssertInViewport(button, scroll);
                TextBlock label = (TextBlock)button.Content!;
                Assert.That(label.Bounds.Width, Is.LessThanOrEqualTo(button.Bounds.Width));
            }
            Capture(window, $"long-labels-220x160-{light}");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertInViewport(Control control, ScrollViewer scroll)
    {
        Point topLeft = control.TranslatePoint(default, scroll)!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(topLeft.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(topLeft.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(topLeft.X + control.Bounds.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 0.001));
            Assert.That(topLeft.Y + control.Bounds.Height, Is.LessThanOrEqualTo(scroll.Viewport.Height + 0.001));
        });
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_SCENE_SETTINGS_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
