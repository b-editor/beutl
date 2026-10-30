using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Beutl.Controls;

/// <summary>
/// A single-line <see cref="TextBlock"/> that trims text it cannot fit, and scrolls it sideways in a loop
/// instead while <see cref="IsMarqueeActive"/> is set.
/// </summary>
/// <remarks>Only <see cref="TextBlock.Text"/> scrolls, laid out and moving left to right; inlines stay trimmed.</remarks>
public class MarqueeTextBlock : TextBlock
{
    public static readonly StyledProperty<bool> IsMarqueeActiveProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, bool>(nameof(IsMarqueeActive));

    public static readonly StyledProperty<double> MarqueeSpeedProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(MarqueeSpeed), 40);

    public static readonly StyledProperty<TimeSpan> MarqueeDelayProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, TimeSpan>(nameof(MarqueeDelay), TimeSpan.FromSeconds(1));

    // The space between the end of the text and the copy that follows it in.
    private const double Gap = 32;

    private TopLevel? _topLevel;
    private TextLayout? _marqueeLayout;
    private double _availableWidth;
    private bool _isScrolling;
    private bool _isFrameRequested;
    private TimeSpan? _startTime;
    private double _offset;

    static MarqueeTextBlock()
    {
        TextTrimmingProperty.OverrideDefaultValue<MarqueeTextBlock>(TextTrimming.CharacterEllipsis);
    }

    public bool IsMarqueeActive
    {
        get => GetValue(IsMarqueeActiveProperty);
        set => SetValue(IsMarqueeActiveProperty, value);
    }

    // In device-independent pixels per second.
    public double MarqueeSpeed
    {
        get => GetValue(MarqueeSpeedProperty);
        set => SetValue(MarqueeSpeedProperty, value);
    }

    // The pause at the start of every pass, while the text keeps its trimmed look.
    public TimeSpan MarqueeDelay
    {
        get => GetValue(MarqueeDelayProperty);
        set => SetValue(MarqueeDelayProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        UpdateScrolling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel = null;
        UpdateScrolling();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMarqueeActiveProperty)
        {
            UpdateScrolling();
        }
        else if (change.Property == TextProperty)
        {
            // A recycled item shows another name, which starts its pass from the beginning.
            _startTime = null;
            _offset = 0;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);

        // Every change to the text or its properties ends in an arrange, which rebuilds the trimmed layout;
        // the untrimmed one follows it here.
        _marqueeLayout?.Dispose();
        _marqueeLayout = null;
        _availableWidth = finalSize.Width - Padding.Left - Padding.Right;
        UpdateScrolling();
        return size;
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        // The pause between passes keeps the trimmed look, so a pointer passing over the text does not change it.
        if (!_isScrolling || _marqueeLayout == null || _offset == 0)
        {
            base.RenderTextLayout(context, origin);
            return;
        }

        double stride = _marqueeLayout.WidthIncludingTrailingWhitespace + Gap;
        using (context.PushClip(new Rect(origin.X, 0, _availableWidth, Bounds.Height)))
        {
            _marqueeLayout.Draw(context, origin.WithX(origin.X - _offset));
            _marqueeLayout.Draw(context, origin.WithX(origin.X - _offset + stride));
        }
    }

    private void UpdateScrolling()
    {
        bool scroll = false;
        if (IsMarqueeActive && _topLevel != null)
        {
            _marqueeLayout ??= new TextLayout(
                Text,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                FontSize,
                Foreground,
                textDecorations: TextDecorations,
                letterSpacing: LetterSpacing,
                fontFeatures: FontFeatures);
            scroll = _marqueeLayout.Width > _availableWidth;
        }

        if (!scroll)
        {
            _marqueeLayout?.Dispose();
            _marqueeLayout = null;
        }

        if (scroll == _isScrolling)
            return;

        _isScrolling = scroll;
        _startTime = null;
        _offset = 0;
        if (scroll)
        {
            RequestFrame();
        }

        InvalidateVisual();
    }

    private void RequestFrame()
    {
        if (_isFrameRequested || _topLevel == null)
            return;

        _isFrameRequested = true;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _isFrameRequested = false;
        if (!_isScrolling || _marqueeLayout == null)
            return;

        // The second copy stands where the first one started once a pass ends, so the passes join seamlessly.
        _startTime ??= time;
        double speed = Math.Max(MarqueeSpeed, 1);
        double delay = MarqueeDelay.TotalSeconds;
        double travel = (_marqueeLayout.WidthIncludingTrailingWhitespace + Gap) / speed;
        double elapsed = (time - _startTime.Value).TotalSeconds % (delay + travel);
        double offset = elapsed <= delay ? 0 : (elapsed - delay) * speed;
        if (offset != _offset)
        {
            _offset = offset;
            InvalidateVisual();
        }

        RequestFrame();
    }
}
