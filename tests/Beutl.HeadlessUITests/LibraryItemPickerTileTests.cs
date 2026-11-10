using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// A picker whose items have thumbnails shows them as two columns of tiles, which the arrow keys move through by
// tile and by row, and switches to the list and back keeping the selection.
[TestFixture]
public class LibraryItemPickerTileTests
{
    private static (Window Window, LibraryItemPickerFlyoutPresenter Presenter) Show(bool canShowTiles, bool showTiles)
    {
        var items = new ReactiveCollection<PinnableLibraryItem>();
        for (int i = 0; i < 7; i++)
        {
            items.Add(new PinnableLibraryItem($"Item {i}", false, i));
        }

        var presenter = new LibraryItemPickerFlyoutPresenter
        {
            Items = items,
            CanShowTiles = canShowTiles,
            ShowTiles = showTiles,
        };
        var window = new Window { Width = 480, Height = 640, Content = presenter };
        window.Show();
        HeadlessTestHelpers.Render(3);
        return (window, presenter);
    }

    private static ListBox Find(LibraryItemPickerFlyoutPresenter presenter, string name)
    {
        return presenter.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == name);
    }

    private static Button? FindToggle(LibraryItemPickerFlyoutPresenter presenter)
    {
        return presenter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .SingleOrDefault(toggle => toggle.Name == "ShowTilesButton");
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        HeadlessTestHelpers.Render();
    }

    [AvaloniaTest]
    public void The_arrow_keys_move_through_the_tiles_by_tile_and_by_row()
    {
        (Window window, LibraryItemPickerFlyoutPresenter presenter) = Show(canShowTiles: true, showTiles: true);
        try
        {
            ListBox tiles = Find(presenter, "PART_TileListBox");
            Assert.Multiple(() =>
            {
                Assert.That(tiles.IsEffectivelyVisible, Is.True);
                Assert.That(Find(presenter, "PART_ListBox").IsEffectivelyVisible, Is.False);
                Assert.That(FindToggle(presenter)!.IsEffectivelyVisible, Is.True);
            });

            presenter.FocusInitialElement();
            HeadlessTestHelpers.Render(2);
            Assert.That(tiles.SelectedIndex, Is.Zero, "Focusing the tiles selects the first.");

            Press(window, Key.Down, PhysicalKey.ArrowDown);
            Assert.That(tiles.SelectedIndex, Is.EqualTo(PickerColumns));
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.That(tiles.SelectedIndex, Is.EqualTo(PickerColumns + 1));
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.That(tiles.SelectedIndex, Is.EqualTo(1));
            Press(window, Key.Up, PhysicalKey.ArrowUp);
            Assert.That(tiles.SelectedIndex, Is.EqualTo(1), "A row up from the first row stays.");
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.That(tiles.SelectedIndex, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }

    private static int PickerColumns => LibraryItemPickerFlyoutPresenter.TileColumns;

    [AvaloniaTest]
    public void Switching_to_the_list_and_back_keeps_the_selection()
    {
        (Window window, LibraryItemPickerFlyoutPresenter presenter) = Show(canShowTiles: true, showTiles: true);
        try
        {
            presenter.SelectedItem = presenter.Items![3];
            HeadlessTestHelpers.Render();

            presenter.ShowTiles = false;
            HeadlessTestHelpers.Render(2);
            ListBox list = Find(presenter, "PART_ListBox");
            Assert.Multiple(() =>
            {
                Assert.That(list.IsEffectivelyVisible, Is.True);
                Assert.That(list.SelectedIndex, Is.EqualTo(3));
            });

            presenter.ShowTiles = true;
            HeadlessTestHelpers.Render(2);
            Assert.That(Find(presenter, "PART_TileListBox").SelectedIndex, Is.EqualTo(3));
        }
        finally
        {
            window.Close();
        }
    }

    // Other pickers, such as one of audio effects or fonts, keep their list, their width and no tile button.
    [AvaloniaTest]
    public void A_picker_without_thumbnails_stays_a_list()
    {
        (Window window, LibraryItemPickerFlyoutPresenter presenter) = Show(canShowTiles: false, showTiles: true);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(Find(presenter, "PART_ListBox").IsEffectivelyVisible, Is.True);
                Assert.That(Find(presenter, "PART_TileListBox").IsEffectivelyVisible, Is.False);
                Assert.That(FindToggle(presenter)?.IsEffectivelyVisible ?? false, Is.False);
                Assert.That(presenter.Bounds.Width, Is.EqualTo(240));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
