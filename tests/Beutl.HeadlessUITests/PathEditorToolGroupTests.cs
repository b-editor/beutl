using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PathEditorToolGroupTests
{
    [AvaloniaTest]
    [TestCase(280, 180, false)]
    [TestCase(280, 180, true)]
    [TestCase(320, 180, false)]
    [TestCase(320, 180, true)]
    [TestCase(640, 180, false)]
    [TestCase(640, 180, true)]
    [TestCase(640, 420, false)]
    [TestCase(640, 420, true)]
    public async Task Tool_groups_expose_names_and_preserve_mouse_and_keyboard_operations(int width, int height, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = System.IO.Path.Combine(BeutlHomeIsolation.CurrentHome!, $"path-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "path-tools", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        using var model = new PathEditorTabViewModel(editor);
        var view = new PathEditorTabView { DataContext = model };
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
            var toolbar = view.FindControl<Beutl.Controls.ToolTabBar>("EditorToolBar")!;
            var viewport = view.FindControl<Panel>("Viewport")!;
            var inspector = view.FindControl<Border>("Inspector")!;
            Assert.That(toolbar.Bounds.Bottom, Is.LessThanOrEqualTo(viewport.Bounds.Top));
            if (width >= 560) Assert.That(viewport.Bounds.Right, Is.LessThanOrEqualTo(inspector.Bounds.Left));
            Button compactSettings = view.FindControl<Button>("CompactSettingsButton")!;
            Assert.That(compactSettings.IsVisible, Is.EqualTo(width < 560));
            if (compactSettings.IsVisible)
            {
                Click(compactSettings);
                HeadlessTestHelpers.Render(3);
            }
            StackPanel modes = view.FindControl<StackPanel>("DragModeTools")!;
            StackPanel display = view.FindControl<StackPanel>("DisplayTools")!;
            Border separator = view.FindControl<Border>("ToolGroupSeparator")!;
            RadioButton[] buttons = modes.Children.OfType<RadioButton>().ToArray();
            ToggleButton stroke = view.FindControl<ToggleButton>("StrokeToggleButton")!;
            ToggleButton fill = view.FindControl<ToggleButton>("FillToggleButton")!;
            ShapePath path = view.FindControl<ShapePath>("path")!;
            var properties = view.FindControl<Border>("PointProperties")!;
            Assert.That(modes.TranslatePoint(new Point(0, modes.Bounds.Height), properties)!.Value.Y,
                Is.LessThan(separator.TranslatePoint(default, properties)!.Value.Y));
            Assert.That(separator.TranslatePoint(new Point(0, separator.Bounds.Height), properties)!.Value.Y,
                Is.LessThan(display.TranslatePoint(default, properties)!.Value.Y));
            foreach (Button button in buttons.Cast<Button>().Concat(new Button[] { stroke, fill }))
            {
                string? tip = ToolTip.GetTip(button) as string;
                Assert.That(tip, Is.Not.Null.And.Not.Empty);
                Assert.That(ControlAutomationPeer.CreatePeerForElement(button)!.GetName(), Is.EqualTo(tip));
                button.BringIntoView();
                HeadlessTestHelpers.Render(3);
                var topLevel = TopLevel.GetTopLevel(button)!;
                Point point = button.TranslatePoint(default, topLevel)!.Value;
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(point.Y + button.Bounds.Height, Is.LessThanOrEqualTo(topLevel.Bounds.Height));
            }

            CheckMode("Symmetry");
            Click(buttons.Single(x => Equals(x.Tag, "Asymmetry")));
            CheckMode("Asymmetry");
            PressSpace(buttons.Single(x => Equals(x.Tag, "Separately")));
            CheckMode("Separately");
            PressSpace(buttons.Single(x => Equals(x.Tag, "Symmetry")));
            CheckMode("Symmetry");

            var strokePeer = (IToggleProvider)ControlAutomationPeer.CreatePeerForElement(stroke)!;
            var fillPeer = (IToggleProvider)ControlAutomationPeer.CreatePeerForElement(fill)!;
            Assert.That(ToolTip.GetTip(stroke), Is.EqualTo(Strings.PathEditor_ShowStroke));
            Assert.That(ToolTip.GetTip(fill), Is.EqualTo(Strings.PathEditor_ShowFill));
            Click(stroke);
            Assert.That(path.Stroke, Is.Null);
            Assert.That(path.Fill, Is.Not.Null);
            Assert.That(strokePeer.ToggleState, Is.EqualTo(ToggleState.Off));
            PressSpace(stroke);
            Assert.That(path.Stroke, Is.Not.Null);
            Assert.That(strokePeer.ToggleState, Is.EqualTo(ToggleState.On));
            Click(fill);
            Assert.That(path.Fill, Is.Null);
            Assert.That(path.Stroke, Is.Not.Null);
            Assert.That(fillPeer.ToggleState, Is.EqualTo(ToggleState.Off));
            PressSpace(fill);
            Assert.That(path.Fill, Is.Not.Null);
            Assert.That(fillPeer.ToggleState, Is.EqualTo(ToggleState.On));

            if (Environment.GetEnvironmentVariable("BEUTL_PATH_TOOLS_CAPTURE") is { Length: > 0 } directory)
            {
                // A sample path makes the outline/fill display controls visible without
                // involving geometry editing, which this toolbar change does not affect.
                path.Data = Geometry.Parse("M 80,45 C 130,10 200,140 270,50 L 270,140 L 80,140 Z");
                window.MouseMove(new Point(width - 1, height - 1));
                HeadlessTestHelpers.Render();
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                image?.Save(System.IO.Path.Combine(directory, $"path-tools-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }

            void CheckMode(string mode)
            {
                Assert.That(buttons.Count(x => x.IsChecked == true), Is.EqualTo(1));
                Assert.That(buttons.Single(x => x.IsChecked == true).Tag, Is.EqualTo(mode));
                Assert.That(model.Symmetry.Value, Is.EqualTo(mode == "Symmetry"));
                Assert.That(model.Asymmetry.Value, Is.EqualTo(mode == "Asymmetry"));
                Assert.That(model.Separately.Value, Is.EqualTo(mode == "Separately"));
            }

            void Click(Button button)
            {
                button.BringIntoView();
                HeadlessTestHelpers.Render(3);
                var topLevel = TopLevel.GetTopLevel(button)!;
                Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), topLevel)!.Value;
                topLevel.MouseMove(point);
                topLevel.MouseDown(point, MouseButton.Left);
                topLevel.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
            }

            void PressSpace(Button button)
            {
                button.BringIntoView();
                HeadlessTestHelpers.Render(3);
                button.Focus(NavigationMethod.Tab);
                Assert.That(button.IsFocused, Is.True);
                TopLevel.GetTopLevel(button)!.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                TopLevel.GetTopLevel(button)!.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                HeadlessTestHelpers.Render();
            }
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }
}
