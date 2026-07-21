using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests;

// Color behaviour the "beutl.dark.border" theme has to hold. Every test here drives the Dark variant,
// which IS the border theme in this harness: TestApp merges its overrides into Dark, mirroring how
// ThemeService applies them in production. FluentAvalonia's own dark ("Dark (Classic)") is not
// reachable from this harness, so nothing here covers it.
[TestFixture]
public class DarkBorderThemeColorTests
{
    // Two complementary guards for the vanishing-checked-tab fix (the rationale lives on
    // ToggleButtonBackgroundIndeterminate in BeutlDarkBorder.axaml): rendering the real style proves
    // the checked state consumes that brush, and the raw-value test below guards the brush is visible.
    // A revert of either half re-breaks it.
    [AvaloniaTest]
    public void Selected_color_type_tab_paints_the_indeterminate_fill_under_the_border_theme()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var toggle = new ToggleButton { IsChecked = true, Content = new TextBlock { Text = "Solid" } };
        var window = new Window
        {
            Content = new StackPanel { Margin = new Thickness(16), Children = { toggle } },
            Width = 200,
            Height = 120,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            Assert.That(toggle.TryFindResource("ColorPickerTypeTransparentToggleButtonStyle", out object? themeObj), Is.True,
                "the flyout tab-button theme should resolve");
            toggle.Theme = (ControlTheme)themeObj!;
            HeadlessTestHelpers.Render(3);

            ContentPresenter presenter = toggle.GetVisualDescendants().OfType<ContentPresenter>()
                .First(c => c.Name == "ContentPresenter");

            Assert.That(presenter.Background, Is.InstanceOf<ISolidColorBrush>(),
                "the selected tab must paint a solid background");
            Color rendered = ((ISolidColorBrush)presenter.Background!).Color;

            Assert.That(rendered.A, Is.GreaterThan(0),
                "the selected tab background must not be transparent under the border theme");
            Assert.That(rendered, Is.EqualTo(ResolveColor(toggle, "ToggleButtonBackgroundIndeterminate", ThemeVariant.Dark)),
                "the selected tab must paint ToggleButtonBackgroundIndeterminate");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Indeterminate_toggle_fill_is_visible_under_the_border_theme()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var probe = new ToggleButton();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            Assert.That(ResolveColor(probe, "ToggleButtonBackgroundIndeterminate", ThemeVariant.Dark).A, Is.GreaterThan(0),
                "ToggleButtonBackgroundIndeterminate must be a visible fill under the border theme "
                + "(it resolved to ControlFillColorTransparentBrush before — the original bug)");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The Indeterminate brushes are literal copies of the list/tree selected surface (the theme holds
    // no shared token layer, so each key mirrors classic dark value-for-value). Nothing else keeps the
    // copies equal, so tie them together here: change one selected-surface value and this catches the
    // two it leaves behind. The state distinction lives in Opacity (the color is the shared accent
    // shade), so both halves are compared.
    [AvaloniaTest]
    public void Indeterminate_toggle_fill_mirrors_the_list_and_tree_selected_surface()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var probe = new ToggleButton();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            Assert.Multiple(() =>
            {
                foreach (string suffix in new[] { "", "PointerOver", "Pressed" })
                {
                    (Color, double) toggle = ResolveFill(probe, $"ToggleButtonBackgroundIndeterminate{suffix}", ThemeVariant.Dark);
                    Assert.That(toggle, Is.EqualTo(ResolveFill(probe, $"ListViewItemBackgroundSelected{suffix}", ThemeVariant.Dark)),
                        $"ToggleButtonBackgroundIndeterminate{suffix} must mirror ListViewItemBackgroundSelected{suffix}");
                    Assert.That(toggle, Is.EqualTo(ResolveFill(probe, $"TreeViewItemBackgroundSelected{suffix}", ThemeVariant.Dark)),
                        $"ToggleButtonBackgroundIndeterminate{suffix} must mirror TreeViewItemBackgroundSelected{suffix}");
                }
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The accent surfaces reference SystemAccentColor* dynamically; in production ThemeService seeds
    // those shades (custom accent first, then the theme's design accent). This harness runs no
    // ThemeService, so the FluentAvaloniaTheme property is set directly.
    [AvaloniaTest]
    public void Custom_accent_retints_the_accent_fill_and_selected_surfaces()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        FluentAvaloniaTheme faTheme = Application.Current.Styles.OfType<FluentAvaloniaTheme>().Single();

        var probe = new Border();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            var custom = Color.FromRgb(0x10, 0x89, 0x3E);
            faTheme.CustomAccentColor = custom;
            HeadlessTestHelpers.Render(1);

            Assert.That(probe.TryFindResource("SystemAccentColorLight1", ThemeVariant.Dark, out object? light1Obj), Is.True,
                "FluentAvalonia should expose the generated Light1 shade");
            var light1 = (Color)light1Obj!;

            Assert.Multiple(() =>
            {
                Assert.That(ResolveColor(probe, "AccentFillColorDefaultBrush", ThemeVariant.Dark), Is.EqualTo(custom),
                    "the accent fill must take the custom accent");
                Assert.That(ResolveColor(probe, "AccentFillColorSecondaryBrush", ThemeVariant.Dark), Is.EqualTo(light1),
                    "the hover accent fill must take the generated Light1 shade");
                Assert.That(ResolveColor(probe, "FocusStrokeColorOuterBrush", ThemeVariant.Dark), Is.EqualTo(light1),
                    "the focus ring must follow the accent");
                Assert.That(ResolveColor(probe, "ToggleButtonBackgroundIndeterminate", ThemeVariant.Dark), Is.EqualTo(light1),
                    "the accent-soft selected surface must follow the accent");
                Assert.That(ResolveColor(probe, "ListViewItemBackgroundSelected", ThemeVariant.Dark), Is.EqualTo(light1),
                    "the list selected surface must follow the accent");
                Assert.That(ResolveColor(probe, "TreeViewItemBackgroundSelected", ThemeVariant.Dark), Is.EqualTo(light1),
                    "the tree selected surface must follow the accent");
            });
        }
        finally
        {
            faTheme.CustomAccentColor = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The disabled text-input family (TextBox / NumberBox / AutoCompleteBox) must resolve the same
    // disabled foreground/background/border as ComboBox — nothing in the theme dictionaries enforces
    // that the two key families stay in step.
    [AvaloniaTest]
    public void Disabled_text_input_matches_disabled_combobox_under_dark_theme()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var textBox = new TextBox { Text = "1920", Width = 160, IsEnabled = false };
        var comboBox = new ComboBox { ItemsSource = new[] { "Spectrum" }, SelectedIndex = 0, Width = 160, IsEnabled = false };

        var window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Margin = new Thickness(16),
                Children = { textBox, comboBox },
            },
            Width = 400,
            Height = 200,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Color comboForeground = ResolveColor(comboBox, "ComboBoxForegroundDisabled", ThemeVariant.Dark);
            Color comboBackground = ResolveColor(comboBox, "ComboBoxBackgroundDisabled", ThemeVariant.Dark);
            Color comboBorder = ResolveColor(comboBox, "ComboBoxBorderBrushDisabled", ThemeVariant.Dark);

            Border border = textBox.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
            TextPresenter presenter = textBox.GetVisualDescendants().OfType<TextPresenter>().First();

            Assert.Multiple(() =>
            {
                Assert.That(ResolveColor(textBox, "TextControlForegroundDisabled", ThemeVariant.Dark), Is.EqualTo(comboForeground),
                    "TextControlForegroundDisabled must equal ComboBoxForegroundDisabled");
                Assert.That(ResolveColor(textBox, "TextControlBackgroundDisabled", ThemeVariant.Dark), Is.EqualTo(comboBackground),
                    "TextControlBackgroundDisabled must equal ComboBoxBackgroundDisabled");
                Assert.That(ResolveColor(textBox, "TextControlBorderBrushDisabled", ThemeVariant.Dark), Is.EqualTo(comboBorder),
                    "TextControlBorderBrushDisabled must equal ComboBoxBorderBrushDisabled");

                // The template must consume those keys, not just have them defined.
                Assert.That(SolidColor(presenter.Foreground, "the rendered disabled TextBox foreground"), Is.EqualTo(comboForeground),
                    "rendered disabled TextBox foreground");
                Assert.That(SolidColor(border.Background, "the rendered disabled TextBox background"), Is.EqualTo(comboBackground),
                    "rendered disabled TextBox background");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // The new dark theme paints the title-bar bottom border with the dock splitter's color so the two
    // dividers read as one; Light and Dark (Classic) keep FluentAvalonia's stroke instead. Dark
    // (Classic) is unreachable from this harness (see the class comment), so only the border-theme
    // coupling and the presence of a non-border fallback are asserted here.
    [AvaloniaTest]
    public void Title_bar_bottom_border_matches_the_dock_splitter_under_the_border_theme()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var probe = new Border();
        var window = new Window { Content = probe, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(1);

            Color splitter = ResolveColor(probe, "DockSplitterIdleBrush", ThemeVariant.Dark);
            Assert.Multiple(() =>
            {
                Assert.That(ResolveColor(probe, "TitleBarBottomBorderBrush", ThemeVariant.Dark), Is.EqualTo(splitter),
                    "the border theme must paint the title-bar bottom border with the dock splitter color");
                Assert.That(splitter.A, Is.GreaterThan(0),
                    "the shared divider color must be visible under the border theme");

                Assert.That(probe.TryFindResource("TitleBarBottomBorderBrush", ThemeVariant.Light, out object? light), Is.True,
                    "non-border themes must still define the title-bar bottom border brush");
                Assert.That(light, Is.InstanceOf<ISolidColorBrush>(),
                    "the non-border fallback must be a solid color brush so the border stays painted");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static Color ResolveColor(Control context, string key, ThemeVariant variant)
    {
        if (!context.TryFindResource(key, variant, out object? value) || value is not ISolidColorBrush brush)
        {
            Assert.Fail($"resource '{key}' should resolve to a solid color brush under {variant} "
                + $"(got {value?.GetType().Name ?? "nothing"})");
            return default;
        }

        return brush.Color;
    }

    private static (Color Color, double Opacity) ResolveFill(Control context, string key, ThemeVariant variant)
    {
        if (!context.TryFindResource(key, variant, out object? value) || value is not ISolidColorBrush brush)
        {
            Assert.Fail($"resource '{key}' should resolve to a solid color brush under {variant} "
                + $"(got {value?.GetType().Name ?? "nothing"})");
            return default;
        }

        return (brush.Color, brush.Opacity);
    }

    private static Color SolidColor(IBrush? brush, string what)
    {
        if (brush is not ISolidColorBrush solid)
        {
            Assert.Fail($"{what} should be a solid color brush (got {brush?.GetType().Name ?? "nothing"})");
            return default;
        }

        return solid.Color;
    }
}
