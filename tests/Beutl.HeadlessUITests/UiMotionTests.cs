using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.Views;
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

    // Inside a node the ports stay on its edge by layout, so the rows only fade rather than move away from them.
    [AvaloniaTest]
    public async Task Content_where_slides_are_off_only_fades_in()
    {
        var toggle = new ToggleButton();
        var content = new Border { Height = 40, Background = Brushes.Gray };
        ExpandTransitionHelper.Attach(toggle, content);
        var host = new StackPanel { Children = { toggle, content } };
        UiMotion.SetSlides(host, false);
        var window = new Window { Content = host, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            toggle.IsChecked = true;
            Assert.That(content.Opacity, Is.EqualTo(0));
            Assert.That(content.RenderTransform, Is.Null);
            await WaitUntil(() => content.Opacity == 1, "The content did not settle open.");
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

    [AvaloniaTest]
    public async Task Pop_scales_and_fades_target_then_clears_its_transform()
    {
        var badge = new Border { Width = 60, Height = 24, Background = Brushes.Gray };
        var window = new Window { Content = badge, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            UiMotion.Pop(badge, UiMotion.Fast);
            Assert.That(badge.Opacity, Is.EqualTo(0));
            Assert.That(badge.RenderTransform, Is.TypeOf<ScaleTransform>());
            await WaitUntil(() => badge.Opacity == 1 && badge.RenderTransform is null, "The popped visual did not settle.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task RevealMotion_animates_only_when_visibility_turns_on_in_a_window()
    {
        var initiallyShown = new Border { Width = 60, Height = 24, Background = Brushes.Gray, IsVisible = true };
        RevealMotion.SetMode(initiallyShown, RevealMotionMode.Pop);
        var popTarget = new Border { Width = 60, Height = 24, Background = Brushes.Gray, IsVisible = false };
        RevealMotion.SetMode(popTarget, RevealMotionMode.Pop);
        var slideTarget = new Border { Width = 60, Height = 24, Background = Brushes.Gray, IsVisible = false };
        RevealMotion.SetMode(slideTarget, RevealMotionMode.Slide);
        var fadeTarget = new Border { Width = 60, Height = 24, Background = Brushes.Gray, IsVisible = false };
        RevealMotion.SetMode(fadeTarget, RevealMotionMode.Fade);

        var window = new Window
        {
            Content = new StackPanel { Children = { initiallyShown, popTarget, slideTarget, fadeTarget } },
            Width = 200,
            Height = 200,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(initiallyShown.Opacity, Is.EqualTo(1), "A visual already visible at attach must not replay.");
            Assert.That(initiallyShown.RenderTransform, Is.Null);

            popTarget.IsVisible = true;
            Assert.That(popTarget.Opacity, Is.EqualTo(0));
            Assert.That(popTarget.RenderTransform, Is.TypeOf<ScaleTransform>());
            await WaitUntil(() => popTarget.Opacity == 1 && popTarget.RenderTransform is null, "Pop target did not settle.");

            slideTarget.IsVisible = true;
            Assert.That(slideTarget.Opacity, Is.EqualTo(0));
            Assert.That(slideTarget.RenderTransform, Is.TypeOf<TranslateTransform>());
            await WaitUntil(() => slideTarget.Opacity == 1 && slideTarget.RenderTransform is null, "Slide target did not settle.");

            fadeTarget.IsVisible = true;
            Assert.That(fadeTarget.Opacity, Is.EqualTo(0));
            Assert.That(fadeTarget.RenderTransform, Is.Null);
            await WaitUntil(() => fadeTarget.Opacity == 1, "Fade target did not settle.");

            // Hiding mid-reveal cancels cleanly and restores opacity and transform.
            popTarget.IsVisible = false;
            popTarget.IsVisible = true;
            Assert.That(popTarget.Opacity, Is.EqualTo(0));
            popTarget.IsVisible = false;
            Assert.That(popTarget.Opacity, Is.EqualTo(1));
            Assert.That(popTarget.RenderTransform, Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void RevealMotion_outside_a_window_does_not_dim_the_control()
    {
        var target = new Border { IsVisible = false };
        RevealMotion.SetMode(target, RevealMotionMode.Pop);

        target.IsVisible = true;
        Assert.That(target.Opacity, Is.EqualTo(1));
        Assert.That(target.RenderTransform, Is.Null);
    }

    [AvaloniaTest]
    public void Clearing_RevealMotion_mode_mid_reveal_restores_opacity_and_transform()
    {
        var target = new Border { Width = 60, Height = 24, Background = Brushes.Gray, IsVisible = false };
        RevealMotion.SetMode(target, RevealMotionMode.Pop);
        var window = new Window { Content = target, Width = 200, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            target.IsVisible = true;
            Assert.That(target.Opacity, Is.EqualTo(0));
            Assert.That(target.RenderTransform, Is.TypeOf<ScaleTransform>());

            RevealMotion.SetMode(target, RevealMotionMode.None);
            Assert.That(target.Opacity, Is.EqualTo(1));
            Assert.That(target.RenderTransform, Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task NodePortPoint_cancels_pending_hover_animation_when_pointer_leaves_before_a_frame()
    {
        var port = new NodePortPoint
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var window = new Window { Content = port, Width = 120, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Point center = HeadlessTestHelpers.Center(port, window);
            // Enter and leave before any animation frame runs: the pending hover run must be cancelled.
            window.MouseMove(center);
            window.MouseMove(new Point(2, 2));

            await Task.Delay(150);
            HeadlessTestHelpers.Render(4);
            Assert.That(port.VisualRadius, Is.EqualTo(5.0).Within(0.01));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ColorPaletteItem_edge_press_keeps_fixed_hit_target_and_fires_click_after_press_animation()
    {
        var swatch = new ColorPaletteItem
        {
            Width = 20,
            Height = 20,
            Color = Colors.CornflowerBlue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        int clicks = 0;
        swatch.Click += (_, _) => clicks++;

        var window = new Window { Content = swatch, Width = 120, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(2);

            Point origin = swatch.TranslatePoint(default, window)!.Value;
            // Press 0.3px inside the left edge (would fall outside a 0.95x RenderTransform shrink).
            var edgePoint = new Point(origin.X + 0.3, origin.Y + 10);
            window.MouseMove(edgePoint);
            window.MouseDown(edgePoint, MouseButton.Left);

            // Let the press animation complete while holding on the edge.
            await Task.Delay(150);
            HeadlessTestHelpers.Render(4);
            Assert.That(swatch.Classes.Contains(":pressed"), Is.True);

            window.MouseUp(edgePoint, MouseButton.Left);
            HeadlessTestHelpers.Render(2);
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(swatch.Classes.Contains(":pressed"), Is.False);
        }
        finally
        {
            window.Close();
        }
    }
}
