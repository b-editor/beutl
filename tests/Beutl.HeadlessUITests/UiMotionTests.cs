using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class UiMotionTests
{
    // Every frame changes the opacity, so headless frames keep coming while the test waits.
    private static Task WaitUntil(Func<bool> condition, string message)
    {
        return HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return condition();
        }, message: message);
    }

    [AvaloniaTest]
    public async Task An_overlay_fades_in_with_its_surface_growing_and_hides_at_once()
    {
        var surface = new Border { Width = 100, Height = 60, Background = Brushes.Gray };
        var overlay = new Panel { IsVisible = false, Children = { surface } };
        OverlayMotion.SetSurface(overlay, surface);
        var window = new Window { Content = overlay, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            OverlayMotion.SetIsOpen(overlay, true);
            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(overlay.Opacity, Is.EqualTo(0));
            Assert.That(surface.RenderTransform, Is.TypeOf<ScaleTransform>());
            await WaitUntil(() => overlay.Opacity == 1 && surface.RenderTransform is null, "The overlay did not settle open.");

            // The overlays empty their lists as they close, so a fade out would show an empty card.
            OverlayMotion.SetIsOpen(overlay, false);
            Assert.That(overlay.IsVisible, Is.False);
            Assert.That(overlay.Opacity, Is.EqualTo(1));

            // Closed before it finished opening, it opens again from the start.
            OverlayMotion.SetIsOpen(overlay, true);
            OverlayMotion.SetIsOpen(overlay, false);
            OverlayMotion.SetIsOpen(overlay, true);
            Assert.That(overlay.Opacity, Is.EqualTo(0));
            await WaitUntil(() => overlay.Opacity == 1, "The reopened overlay did not settle open.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void An_overlay_outside_a_window_opens_and_closes_at_once()
    {
        var overlay = new Panel { IsVisible = false };

        OverlayMotion.SetIsOpen(overlay, true);
        Assert.That(overlay.IsVisible, Is.True);
        Assert.That(overlay.Opacity, Is.EqualTo(1));

        OverlayMotion.SetIsOpen(overlay, false);
        Assert.That(overlay.IsVisible, Is.False);
    }

    [AvaloniaTest]
    public async Task Only_a_row_added_on_its_own_while_shown_is_revealed()
    {
        var items = new ObservableCollection<string> { "a", "b" };
        var list = new ItemsControl { ItemsSource = items };
        ItemsMotion.SetRevealsAddedItems(list, true);
        var window = new Window { Content = list, Width = 200, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(0)!.Opacity, Is.EqualTo(1), "A row shown with the list was revealed.");

            // The same value as a row already there: the new row is the one revealed.
            items.Add("a");
            HeadlessTestHelpers.Render();
            Control added = list.ContainerFromIndex(2)!;
            Assert.That(list.ContainerFromIndex(0)!.Opacity, Is.EqualTo(1), "The earlier row with the same value was revealed.");
            Assert.That(added.Opacity, Is.EqualTo(0));
            Assert.That(added.RenderTransform, Is.TypeOf<TranslateTransform>());
            await WaitUntil(() => added.Opacity == 1 && added.RenderTransform is null, "The added row did not settle.");

            items.Move(0, 2);
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(2)!.Opacity, Is.EqualTo(1), "A moved row was revealed.");

            // A reorder that removes the row and inserts it again.
            string moved = items[0];
            items.RemoveAt(0);
            items.Insert(1, moved);
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(1)!.Opacity, Is.EqualTo(1), "A row inserted again by a reorder was revealed.");

            // The same source filled again, as a list editor does when its list is replaced or first opened.
            items.Clear();
            items.Add("p");
            items.Add("q");
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(0)!.Opacity, Is.EqualTo(1), "A row of a refilled source was revealed.");
            Assert.That(list.ContainerFromIndex(1)!.Opacity, Is.EqualTo(1), "A row of a refilled source was revealed.");

            // A row added while the list is hidden, with the list shown again before the next frame.
            list.IsVisible = false;
            items.Add("r");
            list.IsVisible = true;
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(2)!.Opacity, Is.EqualTo(1), "A row added while hidden was revealed.");

            list.ItemsSource = new ObservableCollection<string> { "x", "y" };
            HeadlessTestHelpers.Render();
            Assert.That(list.ContainerFromIndex(0)!.Opacity, Is.EqualTo(1), "A row of a new source was revealed.");
        }
        finally
        {
            window.Close();
        }
    }

    public sealed class Shown
    {
        public bool IsShown { get; } = true;
    }

    // Content whose visibility is also bound (a list item's rows show only outside presenter mode) gets that value
    // with its data context, after the toggle's first state; it still starts collapsed.
    [AvaloniaTest]
    public void Collapsed_content_stays_hidden_when_its_bound_visibility_arrives_later()
    {
        var toggle = new ToggleButton();
        var content = new Border { Height = 40 };
        content.Bind(Visual.IsVisibleProperty, new Binding(nameof(Shown.IsShown)));
        ExpandTransitionHelper.Attach(toggle, content);
        content.DataContext = new Shown();
        var window = new Window { Content = new StackPanel { Children = { toggle, content } }, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(content.IsVisible, Is.False);
        }
        finally
        {
            window.Close();
        }
    }

    public sealed class Presenter : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isShown;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public bool IsShown
        {
            get => _isShown;
            set
            {
                _isShown = value;
                PropertyChanged?.Invoke(this, new(nameof(IsShown)));
            }
        }
    }

    // A list item leaving presenter mode while collapsed shows its rows through the binding; they stay hidden.
    [AvaloniaTest]
    public void Collapsed_content_stays_hidden_when_its_bound_visibility_turns_on_in_a_window()
    {
        var toggle = new ToggleButton();
        var content = new Border { Height = 40 };
        var presenter = new Presenter();
        content.Bind(Visual.IsVisibleProperty, new Binding(nameof(Presenter.IsShown)));
        content.DataContext = presenter;
        ExpandTransitionHelper.Attach(toggle, content);
        var window = new Window { Content = new StackPanel { Children = { toggle, content } }, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            presenter.IsShown = true;
            HeadlessTestHelpers.Render();
            Assert.That(content.IsVisible, Is.False);

            // The binding still drives it: opened, the rows show.
            toggle.IsChecked = true;
            Assert.That(content.IsVisible, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void A_disposed_helper_leaves_the_content_alone()
    {
        var toggle = new ToggleButton();
        var content = new Border { Height = 40 };
        ExpandTransitionHelper.Attach(toggle, content).Dispose();
        content.IsVisible = true;
        var window = new Window { Content = new StackPanel { Children = { toggle, content } }, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(content.IsVisible, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Expanded_content_moves_into_place_and_collapsed_content_hides()
    {
        var toggle = new ToggleButton();
        var content = new Border { Height = 40, Background = Brushes.Gray };
        ExpandTransitionHelper.Attach(toggle, content);
        Assert.That(content.IsVisible, Is.False, "Collapsed content showed before the window opened.");
        var window = new Window { Content = new StackPanel { Children = { toggle, content } }, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            toggle.IsChecked = true;
            Assert.That(content.IsVisible, Is.True);
            Assert.That(content.RenderTransform, Is.TypeOf<TranslateTransform>());
            await WaitUntil(() => content.Opacity == 1 && content.RenderTransform is null, "The content did not settle open.");

            toggle.IsChecked = false;
            await WaitUntil(() => !content.IsVisible, "The content did not hide.");
            Assert.That(content.Opacity, Is.EqualTo(1));
        }
        finally
        {
            window.Close();
        }
    }
}
