using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

public enum RevealMotionMode
{
    None,
    Slide,
    Fade,
    Pop,
}

// Animates a visual when its IsVisible turns from false to true while shown in a window (tool mode badges,
// shuttle speed pills, view mode switches). Visuals that are already visible when first attached stay put.
public static class RevealMotion
{
    public static readonly AttachedProperty<RevealMotionMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<Visual, RevealMotionMode>("Mode", typeof(RevealMotion));

    private static readonly AttachedProperty<Tracker?> TrackerProperty =
        AvaloniaProperty.RegisterAttached<Visual, Tracker?>("Tracker", typeof(RevealMotion));

    static RevealMotion()
    {
        ModeProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            visual.GetValue(TrackerProperty)?.Dispose();
            RevealMotionMode mode = e.GetNewValue<RevealMotionMode>();
            visual.SetValue(TrackerProperty, mode != RevealMotionMode.None ? new Tracker(visual, mode) : null);
        });
    }

    public static RevealMotionMode GetMode(Visual visual) => visual.GetValue(ModeProperty);

    public static void SetMode(Visual visual, RevealMotionMode value) => visual.SetValue(ModeProperty, value);

    private sealed class Tracker : IDisposable
    {
        private readonly Visual _owner;
        private readonly RevealMotionMode _mode;
        private bool _attached;

        public Tracker(Visual owner, RevealMotionMode mode)
        {
            _owner = owner;
            _mode = mode;
            _attached = TopLevel.GetTopLevel(_owner) is not null;
            _owner.PropertyChanged += OnPropertyChanged;
            _owner.AttachedToVisualTree += OnAttached;
            _owner.DetachedFromVisualTree += OnDetached;
        }

        public void Dispose()
        {
            Settle();
            _owner.PropertyChanged -= OnPropertyChanged;
            _owner.AttachedToVisualTree -= OnAttached;
            _owner.DetachedFromVisualTree -= OnDetached;
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _attached = TopLevel.GetTopLevel(_owner) is not null;
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            Settle();
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty)
                return;

            bool visible = e.GetNewValue<bool>();
            if (!visible)
            {
                Settle();
                return;
            }

            if (!_attached || TopLevel.GetTopLevel(_owner) is null)
                return;

            switch (_mode)
            {
                case RevealMotionMode.Slide:
                    UiMotion.Reveal(_owner, UiMotion.Normal);
                    break;
                case RevealMotionMode.Fade:
                    UiMotion.FadeIn(_owner, UiMotion.Normal);
                    break;
                case RevealMotionMode.Pop:
                    UiMotion.Pop(_owner, UiMotion.Normal);
                    break;
            }
        }

        // Stops a reveal and leaves the visual fully opaque.
        private void Settle()
        {
            UiMotion.Cancel(_owner);
            if (_owner.Opacity < 1)
                _owner.Opacity = 1;
        }
    }
}
