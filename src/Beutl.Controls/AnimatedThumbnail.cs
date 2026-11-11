using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Beutl.Controls;

/// <summary>
/// Shows an <see cref="IAnimatedThumbnailSource"/>: its still while at rest and its frames in a loop while
/// <see cref="IsPlaying"/>, fitted inside the control and centred.
/// </summary>
/// <remarks>
/// It asks for the still only while some of it is on screen, and for the frames only while it plays, so a long
/// list loads what is seen.
/// </remarks>
public sealed class AnimatedThumbnail : Control
{
    public static readonly StyledProperty<IAnimatedThumbnailSource?> SourceProperty =
        AvaloniaProperty.Register<AnimatedThumbnail, IAnimatedThumbnailSource?>(nameof(Source));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AnimatedThumbnail, bool>(nameof(IsPlaying));

    private readonly DispatcherTimer _timer;
    private IAnimatedThumbnailSource? _subscribed;
    private IDisposable? _stillRequest;
    private IDisposable? _framesRequest;
    private bool _onScreen;
    private int _frameIndex;

    static AnimatedThumbnail()
    {
        AffectsRender<AnimatedThumbnail>(SourceProperty, IsPlayingProperty);
    }

    public AnimatedThumbnail()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += OnTick;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public IAnimatedThumbnailSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    // The frame being shown while playing, for tests.
    internal int FrameIndex => _frameIndex;

    // Whether some of it is on screen, so it asks for its still, for tests.
    internal bool IsOnScreen => _onScreen;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        Subscribe(Source);
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        _onScreen = false;
        Subscribe(null);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            // A new source starts from its own requests.
            ReleaseRequests();
            if (VisualRoot != null)
                Subscribe(Source);

            Update();
        }
        else if (change.Property == IsPlayingProperty)
        {
            Update();
        }
    }

    public override void Render(DrawingContext context)
    {
        IAnimatedThumbnailSource? source = _subscribed;
        if (source == null)
            return;

        if (IsPlaying && source.Frames is { } frames)
        {
            int index = Math.Clamp(_frameIndex, 0, frames.Count - 1);
            context.DrawImage(frames.Sheet, frames.GetSourceRect(index), Fit(frames.FrameSize));
        }
        else if (source.Still is { } still)
        {
            context.DrawImage(still, new Rect(still.Size), Fit(still.Size));
        }
    }

    // Uniformly inside the bounds, centred.
    private Rect Fit(Size size)
    {
        Size bounds = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return default;

        double scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
        var fitted = new Size(size.Width * scale, size.Height * scale);
        return new Rect(
            (bounds.Width - fitted.Width) / 2,
            (bounds.Height - fitted.Height) / 2,
            fitted.Width,
            fitted.Height);
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        bool onScreen = e.EffectiveViewport.Intersects(new Rect(Bounds.Size));
        if (onScreen == _onScreen)
            return;

        _onScreen = onScreen;
        Update();
    }

    private void Subscribe(IAnimatedThumbnailSource? source)
    {
        if (ReferenceEquals(source, _subscribed))
            return;

        _subscribed?.Changed -= OnSourceChanged;
        _subscribed = source;
        _subscribed?.Changed += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        Update();
        InvalidateVisual();
    }

    // Brings the requests and the timer in line with what is on screen and whether it plays.
    private void Update()
    {
        IAnimatedThumbnailSource? source = _subscribed;
        bool wantsStill = source != null && _onScreen;
        bool wantsFrames = wantsStill && IsPlaying;

        if (wantsStill)
        {
            _stillRequest ??= source!.RequestStill();
        }
        else
        {
            _stillRequest?.Dispose();
            _stillRequest = null;
        }

        if (wantsFrames)
        {
            _framesRequest ??= source!.RequestFrames();
        }
        else
        {
            _framesRequest?.Dispose();
            _framesRequest = null;
        }

        if (wantsFrames && source!.Frames is { } frames)
        {
            if (!_timer.IsEnabled)
            {
                _frameIndex = frames.FirstIndex;
                _timer.Interval = frames.FrameDuration;
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void ReleaseRequests()
    {
        _stillRequest?.Dispose();
        _stillRequest = null;
        _framesRequest?.Dispose();
        _framesRequest = null;
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_subscribed?.Frames is not { } frames)
        {
            _timer.Stop();
            return;
        }

        _frameIndex = (_frameIndex + 1) % frames.Count;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Sixteen by nine, as wide as it is allowed to be, unless it is given a height to fit instead.
        if (!double.IsInfinity(availableSize.Width))
            return new Size(availableSize.Width, availableSize.Width * 9 / 16);

        if (!double.IsInfinity(availableSize.Height))
            return new Size(availableSize.Height * 16 / 9, availableSize.Height);

        return default;
    }
}
