using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.OutputTab.ViewModels;
using Beutl.Editor.Components.OutputTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Pages.SettingsPages;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PickerFlyoutLayoutTests
{
    private bool _previousOverlayPopups;

    [SetUp]
    public void UseNativePopups()
    {
        _previousOverlayPopups = TestAppBuilder.HeadlessOptions.OverlayPopups;
        TestAppBuilder.HeadlessOptions.OverlayPopups = false;
    }

    [TearDown]
    public void RestorePopupHost() => TestAppBuilder.HeadlessOptions.OverlayPopups = _previousOverlayPopups;

    [AvaloniaTest]
    public async Task Output_picker_grows_when_search_is_shown()
    {
        var (_, _, editor) = await TestShell.OpenNewSceneAsync("output-picker-layout");
        using var model = new OutputTabViewModel(editor);
        var view = new OutputTabView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            view.FindControl<DropDownButton>("ProfilesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var presenter = GetPresenter<OutputPickerFlyoutPresenter>(window);
            double height = TopLevel.GetTopLevel(presenter)!.ClientSize.Height;
            presenter.ShowSearchBox = true;
            VerifyFits(presenter, "output-search");
            Assert.That(TopLevel.GetTopLevel(presenter)!.ClientSize.Height, Is.GreaterThan(height));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("error")]
    [TestCase("multiline")]
    [TestCase("help")]
    public void Expression_editor_grows_with_its_content(string change)
    {
        var shape = new RectShape();
        using var model = new NumberEditorViewModel<float>(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Opacity, shape));
        var menu = new PropertyEditorMenu { DataContext = model };
        var window = CreateWindow(menu);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var contextMenu = (FAMenuFlyout)button.ContextFlyout!;
            contextMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editExpressionItem")
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            contextMenu.Hide();
            var presenter = GetPresenter<ExpressionEditorFlyoutPresenter>(window);
            double height = TopLevel.GetTopLevel(presenter)!.ClientSize.Height;
            TextBox text = presenter.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ExpressionTextBox");
            switch (change)
            {
                case "error":
                    text.Text = "\"abc\"";
                    HeadlessTestHelpers.Click(FindButton(presenter, "AcceptButton"));
                    Assert.That(presenter.ErrorMessage, Is.Not.Empty);
                    break;
                case "multiline":
                    text.Text = string.Join("\n", Enumerable.Repeat("Time +", 12));
                    break;
                case "help":
                    HeadlessTestHelpers.Click(FindButton(presenter, "HelpTabButton"));
                    break;
            }

            VerifyFits(presenter, $"expression-{change}");
            Assert.That(TopLevel.GetTopLevel(presenter)!.ClientSize.Height, Is.GreaterThan(height));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task Marker_note_grows_near_the_marker_after_scrolling_and_zooming()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("marker-picker-layout", MediaTime.FromSeconds(10));
        var marker = new SceneMarker(MediaTime.FromSeconds(0.8), name: "Cue");
        host.Scene.Markers.Add(marker);
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 2, Offset = new System.Numerics.Vector2(75, 0) };
        HeadlessTestHelpers.Render();
        // 0.8 seconds at scale 2 is 240 px; scrolling leaves it at 165 px in the ruler.
        Point point = host.Ruler.TranslatePoint(new Point(165, 8), host.Window)!.Value;
        host.Window.MouseMove(point);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        var presenter = GetPresenter<ContentControl>(host.Window);
        var root = (PopupRoot)TopLevel.GetTopLevel(presenter)!;
        double height = root.ClientSize.Height;
        Assert.That(root.PointToScreen(default).X, Is.EqualTo(host.Ruler.PointToScreen(new Point(165, 0)).X).Within(1));
        presenter.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn).Text =
            string.Join("\n", Enumerable.Range(1, 10).Select(i => $"Note line {i}"));
        VerifyFits(presenter, "marker-note");
        Assert.That(root.ClientSize.Height, Is.GreaterThan(height));
    }

    [AvaloniaTest]
    public async Task Clip_color_picker_grows_when_details_expand()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("clip-color-picker-layout", MediaTime.FromSeconds(10),
            [new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => new RectShape()))]);
        ElementView element = host.View.GetVisualDescendants().OfType<ElementView>().Single();
        element.FindControl<FAMenuFlyoutItem>("changeColor")!.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
        var presenter = GetPresenter<ContentControl>(host.Window);
        double width = TopLevel.GetTopLevel(presenter)!.ClientSize.Width;
        var picker = presenter.GetVisualDescendants().OfType<FAColorPicker>().Single();
        picker.IsCompact = false;
        VerifyFits(presenter, "clip-color-expanded");
        Assert.That(TopLevel.GetTopLevel(presenter)!.ClientSize.Width, Is.GreaterThan(width));
    }

    [AvaloniaTest]
    public async Task Shortcut_picker_grows_for_long_gestures_and_keeps_delete_accessible()
    {
        await TestReset.ResetShellAsync();
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        var page = new KeyMapSettingsPage { DataContext = model.KeyMap };
        var window = CreateWindow(page);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            OptionsDisplayItem group = page.GetVisualDescendants().OfType<OptionsDisplayItem>().First(row => row.Expands);
            group.ContentTransition = null;
            group.IsExpanded = true;
            HeadlessTestHelpers.Render();
            OptionsDisplayItem row = page.GetVisualDescendants().OfType<OptionsDisplayItem>().First(row => row.ActionButton is Button);
            var button = (Button)row.ActionButton!;
            ((KeyMapSettingsItem)button.DataContext!).KeyGesture.Value = new KeyGesture(Key.A);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var presenter = GetPresenter<ContentControl>(window);
            double width = TopLevel.GetTopLevel(presenter)!.ClientSize.Width;
            presenter.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.PageDown,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift
            });
            VerifyFits(presenter, "keymap-long-gesture");
            Assert.That(TopLevel.GetTopLevel(presenter)!.ClientSize.Width, Is.GreaterThan(width));
            var grid = (Grid)presenter.Content!;
            HeadlessTestHelpers.Click(grid.Children.OfType<Button>().Single());
            Assert.That(grid.Children.OfType<TextBox>().Single().Text, Is.EqualTo("(None)"));
        }
        finally { window.Close(); }
    }

    private static Window CreateWindow(Control content) => new()
    {
        Content = content,
        Width = 1000,
        Height = 900,
        RequestedThemeVariant = ThemeVariant.Dark
    };

    private static T GetPresenter<T>(Window window) where T : Control
    {
        HeadlessTestHelpers.Render(3);
        var presenter = (T)window.OpenedPopups.Single(popup => popup.Child is T).Child!;
        Assert.That(TopLevel.GetTopLevel(presenter), Is.InstanceOf<PopupRoot>(),
            "Overlay popups do not reproduce the native window sizing bug.");
        return presenter;
    }

    private static Button FindButton(Control presenter, string name) =>
        presenter.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static void VerifyFits(Control presenter, string name)
    {
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(presenter, nameof(PickerFlyoutLayoutTests), name);
        var root = (PopupRoot)TopLevel.GetTopLevel(presenter)!;
        Assert.Multiple(() =>
        {
            Assert.That(presenter.DesiredSize.Width, Is.LessThanOrEqualTo(root.ClientSize.Width));
            Assert.That(presenter.DesiredSize.Height, Is.LessThanOrEqualTo(root.ClientSize.Height));
            foreach (Button button in presenter.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                Point bottomRight = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), root)!.Value;
                Assert.That(bottomRight.X, Is.LessThanOrEqualTo(root.ClientSize.Width), button.Name);
                Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(root.ClientSize.Height), button.Name);
            }
        });
    }
}
