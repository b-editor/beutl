using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Data;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Controls.NativeContextMenu;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Input;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.HeadlessUITests;

// What a native menu takes from an open Avalonia menu. Showing it takes AppKit: see NativeContextMenuTestHost.
[TestFixture]
public class NativeContextMenuEntryTests
{
    private sealed class Model
    {
        public string Name { get; set; } = "From the data context";
    }

    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    // Opens the flyout at a control in a window and gives what the native menu would read.
    private static void WithOpenFlyout(PopupFlyoutBase flyout, Action<IReadOnlyList<ContextMenuEntry>?> check, Control? target = null)
    {
        target ??= new Border { Width = 200, Height = 100, DataContext = new Model() };
        var window = new Window { Content = target, Width = 400, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            target.ContextFlyout = flyout;
            flyout.ShowAt(target, true);
            HeadlessTestHelpers.Render();
            check(ContextMenuEntry.TryRead((ItemsControl)flyout.Popup.Child!));
        }
        finally
        {
            flyout.Hide();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void An_open_flyout_gives_every_kind_of_item()
    {
        var flyout = new FAMenuFlyout
        {
            Items =
            {
                new FAMenuFlyoutItem { Text = "Plain", InputGesture = new KeyGesture(Key.C, KeyModifiers.Meta) },
                new FAToggleMenuFlyoutItem { Text = "Toggle", IsChecked = true },
                new FARadioMenuFlyoutItem { Text = "Radio", GroupName = nameof(An_open_flyout_gives_every_kind_of_item), IsChecked = true },
                new FAMenuFlyoutItem { Text = "Disabled", IsEnabled = false },
                new FAMenuFlyoutItem { Text = "Hidden", IsVisible = false },
                new FAMenuFlyoutSeparator(),
                new FAMenuFlyoutSubItem
                {
                    Text = "Sub",
                    Items = { new FAMenuFlyoutItem { Text = "Child" }, new FAMenuFlyoutSubItem { Text = "Nested" } }
                }
            }
        };

        WithOpenFlyout(flyout, entries =>
        {
            using var assertions = Assert.EnterMultipleScope();
            Assert.That(entries!.Select(e => e.Title), Is.EqualTo(new[] { "Plain", "Toggle", "Radio", "Disabled", "Hidden", "", "Sub" }));
            Assert.That(entries!.Select(e => e.Kind), Is.EqualTo(new[]
            {
                ContextMenuEntryKind.Item, ContextMenuEntryKind.Item, ContextMenuEntryKind.Item, ContextMenuEntryKind.Item,
                ContextMenuEntryKind.Item, ContextMenuEntryKind.Separator, ContextMenuEntryKind.SubMenu
            }));
            Assert.That(entries![0].Gesture, Is.EqualTo(new KeyGesture(Key.C, KeyModifiers.Meta)));
            Assert.That(entries!.Select(e => e.IsChecked), Is.EqualTo(new[] { false, true, true, false, false, false, false }));
            Assert.That(entries![3].IsEnabled, Is.False);
            Assert.That(entries![4].IsVisible, Is.False);
            Assert.That(entries![6].Children.Select(e => e.Title), Is.EqualTo(new[] { "Child", "Nested" }));
            Assert.That(entries![6].Children[1].Kind, Is.EqualTo(ContextMenuEntryKind.SubMenu));
        });
    }

    [AvaloniaTest]
    public void Invoking_a_toggle_clicks_and_toggles_as_an_avalonia_menu_does()
    {
        int clicks = 0;
        var toggle = new FAToggleMenuFlyoutItem { Text = "Toggle" };
        toggle.Click += (_, _) => clicks++;

        WithOpenFlyout(new FAMenuFlyout { Items = { toggle } }, entries =>
        {
            entries![0].Invoke();

            using var assertions = Assert.EnterMultipleScope();
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(toggle.IsChecked, Is.True);
        });
    }

    [AvaloniaTest]
    public void Invoking_a_menu_item_checks_it_and_raises_its_click()
    {
        int clicks = 0;
        var item = new MenuItem { Header = "Check", ToggleType = MenuItemToggleType.CheckBox };
        item.Click += (_, _) => clicks++;

        WithOpenFlyout(new MenuFlyout { Items = { item } }, entries =>
        {
            entries![0].Invoke();

            using var assertions = Assert.EnterMultipleScope();
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(item.IsChecked, Is.True);
        });
    }

    [AvaloniaTest]
    public void Invoking_a_menu_item_runs_its_command()
    {
        int commands = 0;
        var item = new MenuItem { Header = "Run", Command = new Command(() => commands++) };

        WithOpenFlyout(new MenuFlyout { Items = { item } }, entries =>
        {
            entries![0].Invoke();

            Assert.That(commands, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void Items_in_a_sub_menu_get_their_data_context_though_no_sub_menu_opened()
    {
        var child = new FAMenuFlyoutItem();
        child.Bind(FAMenuFlyoutItem.TextProperty, new Binding(nameof(Model.Name)));
        var flyout = new FAMenuFlyout { Items = { new FAMenuFlyoutSubItem { Text = "Sub", Items = { child } } } };

        WithOpenFlyout(flyout, entries => Assert.That(entries![0].Children[0].Title, Is.EqualTo(new Model().Name)));
    }

    // In Beutl a text box's menu is a command bar, which fills itself as it opens.
    [AvaloniaTest]
    public void The_text_box_menu_gives_the_commands_for_the_selection_with_the_mac_shortcuts()
    {
        var textBox = new TextBox { Text = "Selected", Width = 200 };
        var window = new Window { Content = textBox, Width = 400, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var flyout = (FACommandBarFlyout)textBox.ContextFlyout!;
            textBox.Focus();
            textBox.SelectAll();
            flyout.ShowAt(textBox, true);
            HeadlessTestHelpers.Render();

            IReadOnlyList<ContextMenuEntry> entries = ContextMenuEntry.TryRead(flyout)!;
            ContextMenuEntry copy = entries.Single(e => e.Title == "Copy");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(copy.IsEnabled, Is.True);
                Assert.That(copy.Gesture, Is.EqualTo(new KeyGesture(Key.C, KeyModifiers.Meta)));
                Assert.That(entries.Select(e => e.Title), Does.Contain("Select All"));
            }

            textBox.ClearSelection();
            copy.Invoke();
            flyout.Hide();
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void A_binding_to_a_logical_ancestor_of_the_target_resolves()
    {
        var item = new FAMenuFlyoutItem();
        item.Bind(FAMenuFlyoutItem.TextProperty, new Binding(nameof(Control.Tag))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Border), Tree = TreeType.Logical }
        });
        var child = new FAMenuFlyoutItem();
        child.Bind(FAMenuFlyoutItem.TextProperty, new Binding(nameof(Control.Tag))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(Border), Tree = TreeType.Logical }
        });
        var flyout = new FAMenuFlyout { Items = { item, new FAMenuFlyoutSubItem { Text = "Sub", Items = { child } } } };

        WithOpenFlyout(flyout, entries =>
        {
            using var assertions = Assert.EnterMultipleScope();
            Assert.That(entries![0].Title, Is.EqualTo("Ancestor"));
            Assert.That(entries![1].Children[0].Title, Is.EqualTo("Ancestor"));
        }, new Border { Width = 200, Height = 100, Tag = "Ancestor" });
    }

    // A sub menu built with a template, whose containers only its presenter can make.
    [AvaloniaTest]
    public void A_sub_menu_with_an_items_source_gives_its_containers()
    {
        var subItem = new FAMenuFlyoutSubItem
        {
            Text = "Sub",
            ItemsSource = new[] { "One", "Two" }
        };
        WithOpenFlyout(new FAMenuFlyout { Items = { subItem } }, entries =>
            Assert.That(entries![0].Children.Select(e => e.Title), Is.EqualTo(new[] { "One", "Two" })));
    }

    [AvaloniaTest]
    public void A_header_that_is_not_text_keeps_the_avalonia_menu()
    {
        var flyout = new MenuFlyout { Items = { new MenuItem { Header = new StackPanel() } } };

        WithOpenFlyout(flyout, entries => Assert.That(entries, Is.Null));
    }

    [AvaloniaTest]
    public void A_header_loses_its_access_key_markers()
    {
        var flyout = new MenuFlyout { Items = { new MenuItem { Header = "_Save a__b" } } };

        WithOpenFlyout(flyout, entries => Assert.That(entries![0].Title, Is.EqualTo("Save a_b")));
    }

    // Every menu of a fresh editor with a clip can go native, and the count shows the check reached them.
    [AvaloniaTest]
    public async Task The_menus_of_the_editor_can_all_be_shown_natively()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("native-context-menus");
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromSeconds(2));
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1400, Height = 900 };
        var seen = new HashSet<object>();
        var read = new List<string>();
        var failed = new List<string>();
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);

            foreach (Control control in window.GetVisualDescendants().OfType<Control>().ToArray())
            {
                if (control.ContextFlyout is FAMenuFlyout or MenuFlyout && seen.Add(control.ContextFlyout))
                {
                    var flyout = (PopupFlyoutBase)control.ContextFlyout;
                    flyout.ShowAt(control, true);
                    HeadlessTestHelpers.Render();
                    Record(control, (ItemsControl)flyout.Popup.Child!);
                    flyout.Hide();
                    HeadlessTestHelpers.Settle();
                }
                else if (control.ContextMenu is { } menu && seen.Add(menu))
                {
                    menu.Open(control);
                    HeadlessTestHelpers.Render();
                    Record(control, menu);
                    menu.Close();
                    HeadlessTestHelpers.Settle();
                }
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, read));
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(failed, Is.Empty);
        Assert.That(read, Has.Count.GreaterThan(5));

        void Record(Control control, ItemsControl presenter)
        {
            string name = $"{control.GetType().Name}#{control.Name}";
            if (ContextMenuEntry.TryRead(presenter) is { } entries)
                read.Add($"{name}: {entries.Count} items");
            else
                failed.Add(name);
        }
    }

    // A template image takes only the alpha, so the height shows through it. 28 pixels is the whole image.
    private static int GetVisibleHeight(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        Assert.That(bitmap.PixelSize, Is.EqualTo(new PixelSize(28, 28)));
        var pixels = new byte[28 * 28 * 4];
        unsafe
        {
            fixed (byte* address = pixels)
            {
                bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), (nint)address, pixels.Length, 28 * 4);
            }
        }

        int[] rows = Enumerable.Range(0, 28)
            .Where(y => Enumerable.Range(0, 28).Any(x => pixels[((y * 28) + x) * 4 + 3] > 64))
            .ToArray();
        return rows.Length == 0 ? 0 : rows[^1] - rows[0] + 1;
    }

    [AvaloniaTest]
    public void A_fluent_icon_fills_the_image()
    {
        byte[] png = ContextMenuIcons.TryRender(new FluentIconSource { Icon = FluentIcons.Common.Icon.Copy })!;

        Assert.That(GetVisibleHeight(png), Is.GreaterThanOrEqualTo(24));
    }

    // Below the range it looks tiny next to the other icons, and at the top of it the glyph is cut off.
    [AvaloniaTest]
    public void A_symbol_of_a_standard_command_is_as_large_as_other_icons_without_being_cut_off()
    {
        FAIconSource source = new FAStandardUICommand(FAStandardUICommandKind.Cut).IconSource;

        Assert.That(GetVisibleHeight(ContextMenuIcons.TryRender(source)!), Is.InRange(22, 27));
    }

    [Test]
    public void Only_a_picture_keeps_its_colors()
    {
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(ContextMenuIcons.IsTemplate(new FAImageIconSource()), Is.False);
        Assert.That(ContextMenuIcons.IsTemplate(new FABitmapIconSource()), Is.False);
        Assert.That(ContextMenuIcons.IsTemplate(new FAPathIconSource()), Is.True);
        Assert.That(ContextMenuIcons.IsTemplate(new FluentIconSource()), Is.True);
    }

    // A 120x40 menu at a 100x40 target whose top left is (100, 10).
    [Test]
    [TestCase(PlacementMode.Bottom, 90, 50)]
    [TestCase(PlacementMode.Top, 90, -30)]
    [TestCase(PlacementMode.Left, -20, 10)]
    [TestCase(PlacementMode.Right, 200, 10)]
    [TestCase(PlacementMode.BottomEdgeAlignedLeft, 100, 50)]
    [TestCase(PlacementMode.BottomEdgeAlignedRight, 80, 50)]
    [TestCase(PlacementMode.TopEdgeAlignedLeft, 100, -30)]
    [TestCase(PlacementMode.RightEdgeAlignedTop, 200, 10)]
    [TestCase(PlacementMode.LeftEdgeAlignedBottom, -20, 10)]
    [TestCase(PlacementMode.Center, 90, 10)]
    public void A_menu_goes_where_the_placement_puts_a_popup(PlacementMode placement, double x, double y)
    {
        Point location = ContextMenuLayout.GetLocation(
            placement, PopupAnchor.None, PopupGravity.None, new Rect(100, 10, 100, 40), new Size(120, 40), default);

        Assert.That(location, Is.EqualTo(new Point(x, y)));
    }

    [Test]
    public void Offsets_and_gravity_apply_to_a_menu()
    {
        Point location = ContextMenuLayout.GetLocation(
            PlacementMode.AnchorAndGravity, PopupAnchor.BottomRight, PopupGravity.TopLeft,
            new Rect(0, 0, 100, 40), new Size(30, 20), new Point(1, 2));

        Assert.That(location, Is.EqualTo(new Point(71, 22)));
    }

    [Test]
    [TestCase(Key.C, "c")]
    [TestCase(Key.D5, "5")]
    [TestCase(Key.OemComma, ",")]
    [TestCase(Key.Space, " ")]
    public void Keys_become_key_equivalents(Key key, string expected)
    {
        Assert.That(ContextMenuLayout.GetKeyEquivalent(key), Is.EqualTo(expected));
    }

    [Test]
    public void Keys_without_a_character_use_the_function_key_range()
    {
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(ContextMenuLayout.GetKeyEquivalent(Key.F1)![0], Is.EqualTo((char)0xF704));
        Assert.That(ContextMenuLayout.GetKeyEquivalent(Key.F12)![0], Is.EqualTo((char)0xF70F));
        Assert.That(ContextMenuLayout.GetKeyEquivalent(Key.Delete)![0], Is.EqualTo((char)0xF728));
        Assert.That(ContextMenuLayout.GetKeyEquivalent(Key.Up)![0], Is.EqualTo((char)0xF700));
        Assert.That(ContextMenuLayout.GetKeyEquivalent(Key.None), Is.Null);
    }

    [Test]
    public void Modifiers_become_the_modifier_mask()
    {
        Assert.That(ContextMenuLayout.GetModifierMask(KeyModifiers.Meta | KeyModifiers.Shift), Is.EqualTo((1UL << 20) | (1UL << 17)));
    }
}
