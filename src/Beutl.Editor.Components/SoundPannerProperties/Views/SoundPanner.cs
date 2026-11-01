using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Editor.Components.SoundPannerProperties.ViewModels;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SoundPannerProperties.Views;

public sealed class SoundPanner : Control
{
    public static readonly StyledProperty<SoundPannerViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<SoundPanner, SoundPannerViewModel?>(nameof(ViewModel));

    private static readonly IBrush s_background = new ImmutableSolidColorBrush(Color.FromRgb(30, 32, 36));
    private static readonly IBrush s_speaker = new ImmutableSolidColorBrush(Color.FromRgb(53, 57, 64));
    private static readonly IBrush s_puck = new ImmutableSolidColorBrush(Color.FromRgb(100, 190, 245));
    private static readonly IPen s_grid = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(71, 76, 85)), 1);
    private static readonly IPen s_outline = new ImmutablePen(Brushes.White, 1.5);
    private CompositeDisposable? _subscriptions;
    private SoundPannerViewModel? _dragging;
    private MediaTime _dragTime;
    private SpeakerLayout _dragLayout;
    private bool _dragHorizontalOnly;
    private bool _changed;

    public SoundPannerViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    internal Rect PanningBounds => new(36, 40, Math.Max(1, Bounds.Width - 72), Math.Max(1, Bounds.Height - 80));

    internal Point PuckCenter => ViewModel is { } vm
        ? new Point(PanningBounds.X + (Math.Clamp(vm.Pan.Value, -100, 100) + 100) / 200 * PanningBounds.Width,
            vm.IsHorizontalOnly.Value ? Bounds.Height / 2
                : PanningBounds.Y + Math.Clamp(vm.SurroundPan.Value, 0, 100) / 100 * PanningBounds.Height)
        : PanningBounds.Center;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            FinishDrag();
            Subscribe();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FinishDrag();
        _subscriptions?.Dispose();
        _subscriptions = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        _subscriptions?.Dispose();
        _subscriptions = [];
        if (ViewModel is { } vm)
        {
            vm.Pan.Subscribe(_ => InvalidateVisual()).AddTo(_subscriptions);
            vm.SurroundPan.Subscribe(_ => InvalidateVisual()).AddTo(_subscriptions);
            vm.Layout.Subscribe(_ => InvalidateVisual()).AddTo(_subscriptions);
            vm.IsHorizontalOnly.Subscribe(_ => InvalidateVisual()).AddTo(_subscriptions);
            vm.CanDrag.Subscribe(_ => InvalidateVisual()).AddTo(_subscriptions);
        }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ViewModel is not { CanDrag.Value: true } vm)
            return;
        if (e.ClickCount == 2)
        {
            FinishDrag();
            vm.ResetPosition();
            e.Handled = true;
            return;
        }
        Point offset = e.GetPosition(this) - PuckCenter;
        if (offset.X * offset.X + offset.Y * offset.Y > 18 * 18) return;
        _dragging = vm;
        _dragTime = vm.CurrentTime.Value;
        _dragLayout = vm.Layout.Value;
        _dragHorizontalOnly = vm.IsHorizontalOnly.Value;
        _changed = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging is not { } vm) return;
        // Seeking or changing the layout ends this gesture; it must not start editing another key.
        if (vm.CurrentTime.Value != _dragTime || vm.Layout.Value != _dragLayout
            || vm.IsHorizontalOnly.Value != _dragHorizontalOnly || !vm.CanDrag.Value)
        {
            FinishDrag();
            e.Pointer.Capture(null);
            return;
        }
        Point point = e.GetPosition(this);
        Rect area = PanningBounds;
        _changed |= vm.SetPosition((float)((point.X - area.X) / area.Width * 200 - 100),
            (float)((point.Y - area.Y) / area.Height * 100));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == null) return;
        FinishDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        FinishDrag();
        base.OnPointerCaptureLost(e);
    }

    private void FinishDrag()
    {
        var vm = _dragging;
        _dragging = null;
        if (_changed) vm?.Commit();
        _changed = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (ViewModel is not { } vm || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.DrawRectangle(s_background, s_grid, new RoundedRect(new Rect(Bounds.Size).Deflate(1), 6));
        Rect area = PanningBounds;
        bool stereo = vm.Layout.Value == SpeakerLayout.Stereo;
        bool mono = vm.Layout.Value == SpeakerLayout.Mono;
        using (context.PushOpacity(vm.CanPan.Value ? 1 : 0.45))
        {
            if (!stereo && !mono)
            {
                context.DrawRectangle(null, s_grid, area);
                context.DrawLine(s_grid, new Point(area.Center.X, area.Top), new Point(area.Center.X, area.Bottom));
            }
            context.DrawLine(s_grid, new Point(area.Left, area.Center.Y), new Point(area.Right, area.Center.Y));
            if (mono)
            {
                DrawSpeaker(context, "C", new Point(area.Center.X, 17));
            }
            else if (stereo)
            {
                DrawSpeaker(context, "L", new Point(17, area.Center.Y));
                DrawSpeaker(context, "R", new Point(Bounds.Width - 17, area.Center.Y));
            }
            else
            {
                DrawSpeaker(context, "L", new Point(area.Left, 17));
                DrawSpeaker(context, "C", new Point(area.Center.X, 17));
                DrawSpeaker(context, "R", new Point(area.Right, 17));
                if (vm.Layout.Value == SpeakerLayout.Surround71)
                {
                    DrawSpeaker(context, "Ls", new Point(17, area.Center.Y));
                    DrawSpeaker(context, "Rs", new Point(Bounds.Width - 17, area.Center.Y));
                    DrawSpeaker(context, "Lrs", new Point(area.Left, Bounds.Height - 17));
                    DrawSpeaker(context, "Rrs", new Point(area.Right, Bounds.Height - 17));
                }
                else
                {
                    DrawSpeaker(context, "Ls", new Point(area.Left, Bounds.Height - 17));
                    DrawSpeaker(context, "Rs", new Point(area.Right, Bounds.Height - 17));
                }
            }
            context.DrawEllipse(vm.CanDrag.Value ? s_puck : s_speaker, s_outline, PuckCenter, 8, 8);
        }
    }

    private static void DrawSpeaker(DrawingContext context, string label, Point center)
    {
        context.DrawRectangle(s_speaker, null, new RoundedRect(new Rect(center.X - 14, center.Y - 11, 28, 22), 4));
        var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, Brushes.White);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
