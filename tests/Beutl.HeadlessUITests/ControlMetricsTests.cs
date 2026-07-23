using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// Locks the shared control height: every interactive control that can sit in a horizontal row
// must render at exactly the same height. TextBox-like controls reach it via
// TextControlThemeMinHeight/ComboBoxMinHeight; the button family has no MinHeight (it would
// inflate explicitly sized icon buttons) and relies on padding — partly via default ControlTheme
// overrides in Styles.axaml because FA freezes ButtonPadding via StaticResource — so this test is
// what keeps the row from drifting apart when density resources change.
[TestFixture]
public class ControlMetricsTests
{
    private const double ExpectedControlHeight = 28;
    private const double PanelRowHeight = 32;
    private const double PopupItemHeight = 28;

    [AvaloniaTest]
    public void Row_controls_render_at_the_unified_height()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        // VerticalAlignment=Center keeps each control at its natural height; the default
        // Stretch would mask a mismatch by stretching all controls to the tallest sibling.
        (string Name, Control Control)[] controls =
        [
            ("Button", new Button { Content = "Standard" }),
            ("ToggleButton", new ToggleButton { Content = "Toggle" }),
            ("RepeatButton", new RepeatButton { Content = "Repeat" }),
            ("DropDownButton", new DropDownButton { Content = "Menu" }),
            ("HyperlinkButton", new HyperlinkButton { Content = "Link" }),
            ("TextBox", new TextBox { Text = "1920", Width = 100 }),
            ("ComboBox", new ComboBox { ItemsSource = new[] { "Spectrum" }, SelectedIndex = 0, Width = 120 }),
            ("NumberBox", new NumberBox { Value = 1920, Width = 100 }),
            ("AutoCompleteBox", new AutoCompleteBox { Text = "1920", Width = 100 }),
            ("ColorPickerButton", new ColorPickerButton()),
        ];

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(16),
        };
        foreach ((_, Control control) in controls)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(control);
        }

        var window = new Window
        {
            Content = panel,
            Width = 1400,
            Height = 200,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            string actual = string.Join(", ", controls.Select(c => $"{c.Name}={c.Control.Bounds.Height}"));
            Assert.Multiple(() =>
            {
                foreach ((string name, Control control) in controls)
                {
                    Assert.That(
                        control.Bounds.Height,
                        Is.EqualTo(ExpectedControlHeight).Within(0.5),
                        $"{name} is off the unified height. {actual}");
                }
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Dropdown_glyphs_sit_at_the_unified_right_inset()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        // Two ComboBox instances: with a selection and empty (placeholder path) — they exercise
        // different template branches and must both carry the inset.
        var comboSelected = new ComboBox { ItemsSource = new[] { "Spectrum" }, SelectedIndex = 0, Width = 220 };
        var comboEmpty = new ComboBox { ItemsSource = new[] { "Spectrum" }, Width = 220 };
        var dropDown = new DropDownButton { Content = "Menu" };

        var window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(16),
                Children = { comboSelected, comboEmpty, dropDown },
            },
            Width = 800,
            Height = 160,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            static double GlyphRightInset(Control owner)
            {
                Control glyph = owner.GetVisualDescendants().OfType<Control>()
                    .First(c => c.Name == "DropDownGlyph");
                Point topLeft = glyph.TranslatePoint(new Point(0, 0), owner)!.Value;
                return owner.Bounds.Width - (topLeft.X + glyph.Bounds.Width);
            }

            static double ContentLeftInset(ComboBox owner)
            {
                Control presenter = owner.GetVisualDescendants().OfType<Control>()
                    .First(c => c.Name == "ContentPresenter");
                return presenter.TranslatePoint(new Point(0, 0), owner)!.Value.X;
            }

            Assert.Multiple(() =>
            {
                Assert.That(GlyphRightInset(comboSelected), Is.EqualTo(8).Within(0.5), "ComboBox (selected) glyph inset");
                Assert.That(GlyphRightInset(comboEmpty), Is.EqualTo(8).Within(0.5), "ComboBox (empty) glyph inset");
                Assert.That(GlyphRightInset(dropDown), Is.EqualTo(8).Within(0.5), "DropDownButton glyph inset");
                Assert.That(ContentLeftInset(comboSelected), Is.LessThanOrEqualTo(10.5), "ComboBox (selected) content left inset");
                Assert.That(ContentLeftInset(comboEmpty), Is.LessThanOrEqualTo(10.5), "ComboBox (empty) content left inset");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Panel_item_rows_render_at_32()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var listBox = new ListBox { ItemsSource = new[] { "Templates", "PR", "background.jpg" }, Width = 240 };
        var treeView = new TreeView { Width = 240 };
        treeView.Items.Add(new TreeViewItem { Header = "Folder" });
        treeView.Items.Add(new TreeViewItem { Header = "File" });

        var window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(16),
                Children = { listBox, treeView },
            },
            Width = 640,
            Height = 400,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            ListBoxItem[] listItems = listBox.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            TreeViewItem[] treeItems = treeView.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
            Assert.That(listItems, Is.Not.Empty, "ListBox should realize item containers.");
            Assert.That(treeItems, Is.Not.Empty, "TreeView should realize item containers.");

            Assert.Multiple(() =>
            {
                foreach (ListBoxItem item in listItems)
                {
                    Assert.That(item.Bounds.Height, Is.EqualTo(PanelRowHeight).Within(0.5),
                        $"ListBoxItem heights: {string.Join(", ", listItems.Select(i => i.Bounds.Height))}");
                }

                foreach (TreeViewItem item in treeItems)
                {
                    Assert.That(item.Bounds.Height, Is.EqualTo(PanelRowHeight).Within(0.5),
                        $"TreeViewItem heights: {string.Join(", ", treeItems.Select(i => i.Bounds.Height))}");
                }
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Popup_items_render_at_28()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "Spectrum", "Waveform" },
            SelectedIndex = 0,
            Width = 160,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var flyoutOwner = new Button { Content = "Menu", VerticalAlignment = VerticalAlignment.Top };
        var menuItem = new MenuFlyoutItem { Text = "Cut" };
        var toggleItem = new ToggleMenuFlyoutItem { Text = "Snap", IsChecked = true };
        var radioItem = new RadioMenuFlyoutItem { Text = "Solid" };
        var flyout = new FAMenuFlyout { Items = { menuItem, toggleItem, radioItem } };

        var window = new Window
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(16),
                Children = { comboBox, flyoutOwner },
            },
            Width = 640,
            Height = 400,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            comboBox.IsDropDownOpen = true;
            HeadlessTestHelpers.Render(3);
            ComboBoxItem[] comboItems = comboBox.GetLogicalDescendants().OfType<ComboBoxItem>().ToArray();
            Assert.That(comboItems, Is.Not.Empty, "Opening the dropdown should realize ComboBoxItem containers.");
            Assert.Multiple(() =>
            {
                foreach (ComboBoxItem item in comboItems)
                {
                    Assert.That(item.Bounds.Height, Is.EqualTo(PopupItemHeight).Within(0.5),
                        $"ComboBoxItem heights: {string.Join(", ", comboItems.Select(i => i.Bounds.Height))}");
                }
            });
            comboBox.IsDropDownOpen = false;
            HeadlessTestHelpers.Render(1);

            flyout.ShowAt(flyoutOwner);
            HeadlessTestHelpers.Render(3);
            (string Name, Control Item)[] menuItems =
            [
                ("MenuFlyoutItem", menuItem),
                ("ToggleMenuFlyoutItem", toggleItem),
                ("RadioMenuFlyoutItem", radioItem),
            ];
            string actual = string.Join(", ", menuItems.Select(m => $"{m.Name}={m.Item.Bounds.Height}"));
            Assert.Multiple(() =>
            {
                foreach ((string name, Control item) in menuItems)
                {
                    Assert.That(item.Bounds.Height, Is.EqualTo(PopupItemHeight).Within(0.5),
                        $"{name} is off the popup item height. {actual}");
                }
            });
            flyout.Hide();
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
