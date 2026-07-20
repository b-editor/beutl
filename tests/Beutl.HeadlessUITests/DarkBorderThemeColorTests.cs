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
    // two it leaves behind.
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
                    Color toggle = ResolveColor(probe, $"ToggleButtonBackgroundIndeterminate{suffix}", ThemeVariant.Dark);
                    Assert.That(toggle, Is.EqualTo(ResolveColor(probe, $"ListViewItemBackgroundSelected{suffix}", ThemeVariant.Dark)),
                        $"ToggleButtonBackgroundIndeterminate{suffix} must mirror ListViewItemBackgroundSelected{suffix}");
                    Assert.That(toggle, Is.EqualTo(ResolveColor(probe, $"TreeViewItemBackgroundSelected{suffix}", ThemeVariant.Dark)),
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
