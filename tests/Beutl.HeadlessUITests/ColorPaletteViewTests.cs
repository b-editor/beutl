using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.ColorPaletteServiceTests;
using Color = Avalonia.Media.Color;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class ColorPaletteViewTests
{
    private static readonly Color s_red = Color.FromUInt32(0xFFFF0000);
    private static readonly Color s_teal = Color.FromUInt32(0xFF008080);

    [AvaloniaTest]
    [TestCase("Dark")]
    [TestCase("Light")]
    public void The_view_lists_the_recent_colors_then_the_projects_the_users_and_the_default_palette(string theme)
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service, CurrentColor = s_red };
        using var host = new ControlHost(view, 240, 760);
        host.Window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(view, nameof(ColorPaletteViewTests), theme);

        Assert.Multiple(() =>
        {
            Assert.That(HeaderNames(view), Is.EqualTo(new[]
            {
                Strings.ColorPalette_RecentColors, "Brand", "Mine", Strings.ColorPalette_DefaultPalette
            }));
            Assert.That(Panels(view).Select(p => p.Children.Count), Is.EqualTo(new[] { 2, 2, 1, 140 }));
            Assert.That(ProjectIcons(view), Is.EqualTo(new[] { "Brand" }),
                "Only the project's palette is marked as kept in the project.");
            Assert.That(view.GetVisualDescendants().OfType<Button>().Count(b => b.Name == "NewPaletteButton"), Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_click_on_a_color_selects_it()
    {
        var view = new ColorPaletteView { Service = CreateService() };
        var selected = new List<Color>();
        view.ColorSelected += (_, color) => selected.Add(color);
        using var host = new ControlHost(view, 240, 760);
        ColorPaletteItem teal = Swatches(view).First(i => i.Color == s_teal);

        TimelineTestHost.WaitForHitTest(host.Window, teal, HeadlessTestHelpers.Center(teal, host.Window));
        HeadlessTestHelpers.Click(teal);

        Assert.That(selected, Is.EqualTo(new[] { s_teal }));
    }

    // Tab stops once in each palette, the arrow keys move between its colors in rows of ten, and Enter or Space
    // picks the focused color, which a focus ring marks.
    [AvaloniaTest]
    [TestCase("Dark")]
    [TestCase("Light")]
    public void The_keyboard_moves_between_the_colors_and_picks_one(string theme)
    {
        var view = new ColorPaletteView { Service = CreateService() };
        var selected = new List<Color>();
        view.ColorSelected += (_, color) => selected.Add(color);
        using var host = new ControlHost(view, 240, 760);
        Window window = host.Window;
        window.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        ColorSwatchPanel[] panels = Panels(view);
        Button newPalette = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewPaletteButton");

        newPalette.Focus(NavigationMethod.Tab);
        Press(window, Key.Tab);
        Assert.That(Focused(window), Is.SameAs(panels[0].Children[0]), "Tab enters the recent colors at the first.");

        Press(window, Key.Right);
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(view, nameof(ColorPaletteViewTests), $"keyboard-focus-{theme}");
        Assert.That(Focused(window), Is.SameAs(panels[0].Children[1]));
        Press(window, Key.Enter);
        Press(window, Key.Space);
        Assert.That(selected, Is.EqualTo(new[] { s_teal, s_teal }));

        Press(window, Key.Tab);
        Assert.That(panels[0].Children, Does.Not.Contain(Focused(window)), "The next Tab leaves the palette.");

        panels[3].Children[0].Focus(NavigationMethod.Tab);
        Press(window, Key.Down);
        Assert.That(Focused(window), Is.SameAs(panels[3].Children[10]));
        Press(window, Key.Left);
        Assert.That(Focused(window), Is.SameAs(panels[3].Children[9]));
        Press(window, Key.Up);
        Assert.That(Focused(window), Is.SameAs(panels[3].Children[9]), "The first row has nothing above it.");
    }

    // The menu key opens the focused control's menu at that control, wherever the pointer is: a color's own menu,
    // or for a header's button, its palette's menu.
    [AvaloniaTest]
    [TestCase("color")]
    [TestCase("header button")]
    public void The_menu_key_opens_a_menu_at_the_focused_control(string target)
    {
        var view = new ColorPaletteView { Service = CreateService() };
        using var host = new ControlHost(view, 240, 760);
        Control focused = target == "color"
            ? Panels(view)[2].Children[0]
            : Header(view, "Mine").GetVisualDescendants().OfType<Button>()
                .Single(b => (string?)ToolTip.GetTip(b) == Strings.ColorPalette_AddCurrentColor);
        host.Window.MouseMove(new Point(230, 750));

        focused.Focus(NavigationMethod.Tab);
        Press(host.Window, Key.Apps);
        HeadlessTestHelpers.Render(3);
        if (target == "color")
        {
            HeadlessCapture.SaveIfRequested(view, nameof(ColorPaletteViewTests), "keyboard-menu");
        }

        FAMenuFlyoutPresenter menu = host.Window.GetVisualDescendants().OfType<FAMenuFlyoutPresenter>().Single();
        Popup? popup = menu.FindLogicalAncestorOfType<Popup>();
        Assert.Multiple(() =>
        {
            Assert.That(menu.GetVisualDescendants().OfType<FAMenuFlyoutItem>().Select(i => i.Text), Does.Contain(
                target == "color" ? Strings.ColorPalette_ReplaceWithCurrentColor : Strings.ColorPalette_SaveInProject));
            Assert.That(popup?.PlacementTarget, Is.SameAs(focused));
            Assert.That(popup?.Placement, Is.Not.EqualTo(PlacementMode.Pointer));
        });
    }

    [AvaloniaTest]
    public void The_colors_equal_to_the_current_one_are_ringed()
    {
        var view = new ColorPaletteView { Service = CreateService() };
        using var host = new ControlHost(view, 240, 760);
        HeadlessTestHelpers.Render();

        view.CurrentColor = s_red;

        Assert.That(Swatches(view).Where(i => i.IsSelected).Select(i => i.Color), Is.All.EqualTo(s_red));
        // The recent colors, the project's palette and the default palette each have red.
        Assert.That(Swatches(view).Count(i => i.IsSelected), Is.EqualTo(3));
    }

    [AvaloniaTest]
    public void The_plus_of_a_palette_adds_the_current_color_to_it()
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service, CurrentColor = s_teal };
        using var host = new ControlHost(view, 240, 760);
        Button add = Header(view, "Mine").GetVisualDescendants().OfType<Button>()
            .Single(b => (string?)ToolTip.GetTip(b) == Strings.ColorPalette_AddCurrentColor);

        TimelineTestHost.WaitForHitTest(host.Window, add, HeadlessTestHelpers.Center(add, host.Window));
        HeadlessTestHelpers.Click(add);

        Assert.That(service.UserPalettes[0].Colors.Select(c => c.Argb),
            Is.EqualTo(new[] { 0xFF0000FFu, s_teal.ToUInt32() }));
        Assert.That(Panels(view).Select(p => p.Children.Count), Is.EqualTo(new[] { 2, 2, 2, 140 }),
            "The view shows the color it added.");
    }

    // The name box opens with "Palette N" chosen, so that Enter alone keeps it and typing replaces it.
    [AvaloniaTest]
    public void A_new_palette_takes_the_name_typed_for_it()
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service };
        using var host = new ControlHost(view, 240, 760);
        Button button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewPaletteButton");

        TimelineTestHost.WaitForHitTest(host.Window, button, HeadlessTestHelpers.Center(button, host.Window));
        HeadlessTestHelpers.Click(button, ticks: 3);
        TextBox name = host.Window.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.That(name.Text, Is.EqualTo(
            string.Format(CultureInfo.CurrentCulture, Strings.ColorPalette_NewPaletteName, 1)));

        AssertTakesTyping(host.Window, name);
        host.Window.KeyTextInput("Skin tones");
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render(3);

        Assert.That(service.UserPalettes.Select(p => p.Name), Is.EqualTo(new[] { "Mine", "Skin tones" }));
        Assert.That(host.Window.GetVisualDescendants().OfType<TextBox>(), Is.Empty, "Enter closes the name box.");
        Assert.That(HeaderNames(view), Does.Contain("Skin tones"));
    }

    // Enter in the name box confirms it, but a focused button takes Enter itself, as cancel does to cancel.
    [AvaloniaTest]
    public void Enter_on_the_cancel_button_of_the_name_box_cancels()
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service };
        using var host = new ControlHost(view, 240, 760);
        Button button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewPaletteButton");

        TimelineTestHost.WaitForHitTest(host.Window, button, HeadlessTestHelpers.Center(button, host.Window));
        HeadlessTestHelpers.Click(button, ticks: 3);
        Button cancel = host.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DismissButton");
        cancel.Focus();
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render(3);

        Assert.That(service.UserPalettes.Select(p => p.Name), Is.EqualTo(new[] { "Mine" }));
        Assert.That(host.Window.GetVisualDescendants().OfType<TextBox>(), Is.Empty, "Cancel closes the name box.");
    }

    [AvaloniaTest]
    public void A_palette_is_renamed_from_its_menu()
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service };
        using var host = new ControlHost(view, 240, 760);
        Button actions = Header(view, "Mine").GetVisualDescendants().OfType<Button>()
            .Single(b => (string?)ToolTip.GetTip(b) == Strings.ColorPalette_PaletteActions);

        ClickWhenReachable(host.Window, actions);
        HeadlessCapture.SaveIfRequested(view, nameof(ColorPaletteViewTests), "palette-menu");
        ClickWhenReachable(host.Window, MenuItem(host.Window, Strings.Rename));
        Rename(host.Window, expected: "Mine", typed: "Brand colors");

        Assert.That(service.UserPalettes.Select(p => p.Name), Is.EqualTo(new[] { "Brand colors" }));
        Assert.That(HeaderNames(view), Does.Contain("Brand colors"));
    }

    // A color of the user's is named from its context menu, and the name shows in its tip.
    [AvaloniaTest]
    public void A_color_is_named_from_its_context_menu()
    {
        ColorPaletteService service = CreateService();
        var view = new ColorPaletteView { Service = service };
        using var host = new ControlHost(view, 240, 760);
        ColorPaletteItem blue = Swatches(view).Single(i => i.Color == Color.FromUInt32(0xFF0000FF) && i.Tag != null
            && Panels(view)[2].Children.Contains(i));

        TimelineTestHost.WaitForHitTest(host.Window, blue, HeadlessTestHelpers.Center(blue, host.Window));
        HeadlessTestHelpers.Click(blue, button: MouseButton.Right, ticks: 3);
        HeadlessCapture.SaveIfRequested(view, nameof(ColorPaletteViewTests), "color-menu");
        ClickWhenReachable(host.Window, MenuItem(host.Window, Strings.Rename));
        Rename(host.Window, expected: "Blue", typed: "Link");

        Assert.That(service.UserPalettes[0].Colors, Is.EqualTo(new[] { new PaletteColor(0xFF0000FF, "Link") }));
        ColorPaletteItem renamed = Panels(view)[2].Children.OfType<ColorPaletteItem>().Single();
        Assert.That(ToolTip.GetTip(renamed), Is.EqualTo("Link\n#0000FF"));
    }

    private static FAMenuFlyoutItem MenuItem(Window window, string text)
        => window.GetVisualDescendants().OfType<FAMenuFlyoutItem>().Single(i => i.Text == text);

    private static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    // The name box opens focused with its name chosen, so that what is typed replaces the name.
    private static void AssertTakesTyping(Window window, TextBox name)
    {
        Assert.Multiple(() =>
        {
            Assert.That(window.FocusManager?.GetFocusedElement(), Is.SameAs(name));
            Assert.That((name.SelectionStart, name.SelectionEnd), Is.EqualTo((0, name.Text?.Length ?? 0)));
        });
    }

    // Types a name into the open name box, which shows the expected name first, and confirms it with Enter.
    private static void Rename(Window window, string expected, string typed)
    {
        TextBox name = window.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.That(name.Text, Is.EqualTo(expected));
        AssertTakesTyping(window, name);
        window.KeyTextInput(typed);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render(3);
    }

    // The picker shows the color it applied, so that a drag that follows starts from it, and closing the picker
    // makes it a recent color.
    [AvaloniaTest]
    public void A_color_picked_in_the_palette_tab_of_a_color_editor_applies_and_becomes_recent()
    {
        ColorPaletteService previous = ColorPaletteService.Instance;
        var service = new ColorPaletteService(new TestPreferences());
        ColorPaletteService.Instance = service;
        var editor = new ColorEditor
        {
            Header = "Color",
            Value = Color.FromUInt32(0xFF000000),
            IsLivePreviewEnabled = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        var window = new Window { Content = editor, Width = 1000, Height = 900 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button open = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ColorPickerButton");
            ClickWhenReachable(window, open);
            var presenter = window.GetVisualDescendants().OfType<SimpleColorPickerFlyoutPresenter>().Single();
            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "PaletteTabButton"));
            HeadlessCapture.SaveIfRequested(presenter, nameof(ColorPaletteViewTests), "color-editor-palette-tab");
            ColorPaletteItem teal = presenter.GetVisualDescendants().OfType<ColorPaletteItem>().First(i => i.Color == s_teal);
            teal.BringIntoView();
            ClickWhenReachable(window, teal);

            Assert.That(editor.Value, Is.EqualTo(s_teal));
            Assert.That((Color)((SimpleColorPicker)presenter.Content!).Color, Is.EqualTo(s_teal));
            Assert.That(teal.IsSelected, Is.True);
            Assert.That(service.RecentColors, Is.Empty, "A color is recent once the picker closes with it.");

            ClickWhenReachable(window, open);
            Assert.That(service.RecentColors, Is.EqualTo(new[] { s_teal }));
        }
        finally
        {
            window.Close();
            ColorPaletteService.Instance = previous;
        }

    }

    // A pick in the brush editor's palette applies to the solid brush and returns to its tab.
    [AvaloniaTest]
    public void A_color_picked_in_the_palette_tab_of_a_brush_editor_applies_and_becomes_recent()
    {
        ColorPaletteService previous = ColorPaletteService.Instance;
        var service = new ColorPaletteService(new TestPreferences());
        ColorPaletteService.Instance = service;
        var brush = new Beutl.Media.SolidColorBrush(Beutl.Media.Colors.White);
        var shape = new Beutl.Graphics.Shapes.RectShape { Fill = { Value = brush } };
        using var history = new PropertyEditorTestHost.HistoryScope(shape);
        using var viewModel = new Beutl.Editor.Components.PropertyEditors.ViewModels.BrushEditorViewModel(
            new Beutl.PropertyAdapters.SimplePropertyAdapter<Beutl.Media.Brush?>(
                (Beutl.SimpleProperty<Beutl.Media.Brush?>)shape.Fill, shape));
        viewModel.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var editor = new Beutl.Editor.Components.PropertyEditors.Views.BrushEditor
        {
            DataContext = viewModel,
            Width = 360,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        var window = new Window { Content = editor, Width = 900, Height = 900 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ClickWhenReachable(window, editor.BrushPreviewButton);
            var presenter = window.GetVisualDescendants().OfType<BrushEditorFlyoutPresenter>().Single();
            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(b => b.Name == "PaletteTabButton"));
            ColorPaletteItem teal = presenter.GetVisualDescendants().OfType<ColorPaletteItem>().First(i => i.Color == s_teal);
            teal.BringIntoView();
            ClickWhenReachable(window, teal);

            Assert.That(brush.Color.Value, Is.EqualTo(Beutl.Media.Color.FromUInt32(s_teal.ToUInt32())));
            Assert.That(presenter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(b => b.Name == "SolidBrushTabButton").IsChecked, Is.True);

            ClickWhenReachable(window, editor.BrushPreviewButton);
            Assert.That(service.RecentColors, Is.EqualTo(new[] { s_teal }));
        }
        finally
        {
            window.Close();
            ColorPaletteService.Instance = previous;
        }
    }

    // Without a brush, a pick in the palette makes a solid brush of that color, as the solid tab and a pick in it do.
    [AvaloniaTest]
    public void A_color_picked_in_the_palette_tab_without_a_brush_makes_a_solid_brush_of_it()
    {
        ColorPaletteService previous = ColorPaletteService.Instance;
        var service = new ColorPaletteService(new TestPreferences());
        ColorPaletteService.Instance = service;
        var shape = new Beutl.Graphics.Shapes.RectShape { Fill = { Value = null } };
        using var history = new PropertyEditorTestHost.HistoryScope(shape);
        using var viewModel = new Beutl.Editor.Components.PropertyEditors.ViewModels.BrushEditorViewModel(
            new Beutl.PropertyAdapters.SimplePropertyAdapter<Beutl.Media.Brush?>(
                (Beutl.SimpleProperty<Beutl.Media.Brush?>)shape.Fill, shape));
        viewModel.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var editor = new Beutl.Editor.Components.PropertyEditors.Views.BrushEditor
        {
            DataContext = viewModel,
            Width = 360,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        var window = new Window { Content = editor, Width = 900, Height = 900 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ClickWhenReachable(window, editor.BrushPreviewButton);
            var presenter = window.GetVisualDescendants().OfType<BrushEditorFlyoutPresenter>().Single();
            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(b => b.Name == "PaletteTabButton"));
            ColorPaletteItem teal = presenter.GetVisualDescendants().OfType<ColorPaletteItem>().First(i => i.Color == s_teal);
            teal.BringIntoView();
            ClickWhenReachable(window, teal);

            Assert.That(shape.Fill.Value, Is.TypeOf<Beutl.Media.SolidColorBrush>());
            Assert.That(((Beutl.Media.SolidColorBrush)shape.Fill.Value!).Color.Value,
                Is.EqualTo(Beutl.Media.Color.FromUInt32(s_teal.ToUInt32())));
            Assert.That(presenter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                .Single(b => b.Name == "SolidBrushTabButton").IsChecked, Is.True);

            ClickWhenReachable(window, editor.BrushPreviewButton);
            Assert.That(service.RecentColors, Is.EqualTo(new[] { s_teal }));
        }
        finally
        {
            window.Close();
            ColorPaletteService.Instance = previous;
        }
    }

    // The color buttons of layers, markers, gradient stops and settings open Beutl's picker, palettes included. A
    // layer's color is opaque, and the button applies the color confirmed, not the one picked before.
    [AvaloniaTest]
    public void A_color_button_without_alpha_applies_the_confirmed_color_as_opaque()
    {
        ColorPaletteService previous = ColorPaletteService.Instance;
        var service = new ColorPaletteService(new TestPreferences());
        ColorPaletteService.Instance = service;
        Color black = Color.FromUInt32(0xFF000000);
        var button = new SimpleColorPickerButton
        {
            Color = black,
            IsAlphaEnabled = false,
            Margin = new Thickness(20),
        };
        var confirmed = new List<(Color OldValue, Color NewValue)>();
        button.ColorConfirmed += (_, args) => confirmed.Add(args);
        var window = new Window { Content = button, Width = 600, Height = 900, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ClickWhenReachable(window, button.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Button"));
            var presenter = window.GetVisualDescendants().OfType<SimpleColorPickerFlyoutPresenter>().Single();
            var picker = (SimpleColorPicker)presenter.Content!;
            Assert.That(presenter.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "OpacityBox").IsEffectivelyVisible,
                Is.False, "A picker without alpha shows no opacity.");

            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "PaletteTabButton"));
            HeadlessCapture.SaveIfRequested(presenter, nameof(ColorPaletteViewTests), "color-button-palette-tab");
            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<ColorPaletteItem>().First(i => i.Color.A == 0));
            Assert.That((Color)picker.Color, Is.EqualTo(Color.FromUInt32(0xFFFFFFFF)), "Transparent picks its color, opaque.");

            ColorPaletteItem teal = presenter.GetVisualDescendants().OfType<ColorPaletteItem>().First(i => i.Color == s_teal);
            teal.BringIntoView();
            ClickWhenReachable(window, teal);
            Assert.That(button.Color, Is.EqualTo(black), "Nothing applies before the picker is confirmed.");

            ClickWhenReachable(window, presenter.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton"));
            Assert.Multiple(() =>
            {
                Assert.That(button.Color, Is.EqualTo(s_teal));
                Assert.That(confirmed, Is.EqualTo(new[] { (black, s_teal) }));
                Assert.That(service.RecentColors, Is.EqualTo(new[] { s_teal }));
                Assert.That(window.GetVisualDescendants().OfType<SimpleColorPickerFlyoutPresenter>(), Is.Empty);
            });
        }
        finally
        {
            window.Close();
            ColorPaletteService.Instance = previous;
        }
    }

    // Moves the pointer onto the control first: a slow frame lets the tooltip of what it rested on before open, and
    // that tooltip can cover a picker that opened under it.
    private static void ClickWhenReachable(Window window, Control control)
    {
        Point center = HeadlessTestHelpers.Center(control, window);
        window.MouseMove(center);
        TimelineTestHost.WaitForHitTest(window, control, center);
        HeadlessTestHelpers.Click(control, ticks: 3);
    }

    // Recent colors red and teal, a project palette "Brand" with red and teal, and a palette "Mine" with blue.
    private static ColorPaletteService CreateService()
    {
        var service = new ColorPaletteService(new TestPreferences());
        service.AttachProjectHost(new TestProjectHost(
            [new ColorPalette("Brand", [new PaletteColor(s_red.ToUInt32(), "Red"), new PaletteColor(s_teal.ToUInt32())])]));
        service.AddPalette(PaletteScope.User, new ColorPalette("Mine", [new PaletteColor(0xFF0000FF, "Blue")]));
        service.AddRecentColor(s_teal);
        service.AddRecentColor(s_red);
        return service;
    }

    private static IEnumerable<Grid> Headers(ColorPaletteView view)
        => view.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("palette-header"));

    private static Grid Header(ColorPaletteView view, string name)
        => Headers(view).Single(g => NameOf(g) == name);

    private static string? NameOf(Grid header)
        => header.Children.OfType<TextBlock>().Single(t => t.Classes.Contains("palette-name")).Text;

    private static string?[] HeaderNames(ColorPaletteView view) => [.. Headers(view).Select(NameOf)];

    private static string?[] ProjectIcons(ColorPaletteView view)
        => [.. Headers(view).Where(g => g.Children.Any(c => c.Classes.Contains("palette-project-icon"))).Select(NameOf)];

    private static ColorSwatchPanel[] Panels(ColorPaletteView view)
        => [.. view.GetVisualDescendants().OfType<ColorSwatchPanel>()];

    private static IEnumerable<ColorPaletteItem> Swatches(ColorPaletteView view)
        => view.GetVisualDescendants().OfType<ColorPaletteItem>();
}
