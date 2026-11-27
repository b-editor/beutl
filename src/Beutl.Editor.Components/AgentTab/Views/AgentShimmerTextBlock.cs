using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentShimmerTextBlock : TextBlock
{
    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, bool>(nameof(IsLive));
    public static readonly StyledProperty<IBrush> MutedBrushProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, IBrush>(nameof(MutedBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> HighlightBrushProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, IBrush>(nameof(HighlightBrush), Brushes.White);

    private static readonly HashSet<AgentShimmerTextBlock> s_active = [];
    private static DispatcherTimer? s_timer;

    private readonly List<IDisposable> _visibilitySubscriptions = [];
    private LinearGradientBrush? _maskBrush;
    private TextLayout? _highlightLayout;
    private double _renderedWidth;
    private long _started;
    private bool _running;
    private bool _subscribingVisibility;

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }
    public IBrush MutedBrush { get => GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    internal LinearGradientBrush? ShimmerMaskBrush => _maskBrush;
    internal double? TestPhaseOverride { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearVisibilitySubscriptions();
        StopAnimation();
        DisposeHighlightLayout();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLiveProperty || change.Property == IsVisibleProperty
            || change.Property == MutedBrushProperty || change.Property == HighlightBrushProperty)
        {
            if (change.Property == HighlightBrushProperty)
                DisposeHighlightLayout();
            RefreshAnimation();
        }
        else if (change.Property == TextProperty || change.Property == FontFamilyProperty
            || change.Property == FontSizeProperty || change.Property == FontStyleProperty
            || change.Property == FontWeightProperty || change.Property == FontStretchProperty
            || change.Property == TextAlignmentProperty || change.Property == TextWrappingProperty
            || change.Property == TextTrimmingProperty || change.Property == TextDecorationsProperty
            || change.Property == FlowDirectionProperty || change.Property == LineHeightProperty
            || change.Property == LetterSpacingProperty || change.Property == MaxLinesProperty
            || change.Property == FontFeaturesProperty || change.Property == PaddingProperty)
        {
            DisposeHighlightLayout();
        }
        else if (_running && change.Property == BoundsProperty)
        {
            DisposeHighlightLayout();
            UpdateMask(Stopwatch.GetTimestamp());
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        DisposeHighlightLayout();
        return size;
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        base.RenderTextLayout(context, origin);
        if (!_running || _maskBrush is null || string.IsNullOrEmpty(Text))
            return;

        TextLayout? highlight = GetOrCreateHighlightLayout();
        if (highlight is null)
            return;

        double width = Math.Max(TextLayout.WidthIncludingTrailingWhitespace, 1);
        double height = Math.Max(TextLayout.Height, 1);
        _renderedWidth = width;

        // Draw the solid highlight text through a single control-wide opacity mask so Avalonia's
        // per-run glyph translation in TextLineImpl cannot restart the gradient on each word/script run.
        var maskBounds = new Rect(origin.X, origin.Y, width, height);
        using (context.PushOpacityMask(_maskBrush, maskBounds))
        {
            highlight.Draw(context, origin);
        }
    }

    private TextLayout? GetOrCreateHighlightLayout()
    {
        if (string.IsNullOrEmpty(Text))
            return null;

        return _highlightLayout ??= new TextLayout(
            Text,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            HighlightBrush,
            textAlignment: TextAlignment,
            textWrapping: TextWrapping,
            textTrimming: TextTrimming,
            textDecorations: TextDecorations,
            flowDirection: FlowDirection,
            maxWidth: TextLayout.MaxWidth,
            maxHeight: TextLayout.MaxHeight,
            lineHeight: LineHeight,
            letterSpacing: LetterSpacing,
            maxLines: MaxLines,
            fontFeatures: FontFeatures);
    }

    private void DisposeHighlightLayout()
    {
        _highlightLayout?.Dispose();
        _highlightLayout = null;
    }

    private void EnsureVisibilitySubscriptions()
    {
        if (_subscribingVisibility || _visibilitySubscriptions.Count > 0 || !this.IsAttachedToVisualTree())
            return;
        _subscribingVisibility = true;
        try
        {
            foreach (var ancestor in this.GetVisualAncestors())
            {
                _visibilitySubscriptions.Add(ancestor.GetObservable(IsVisibleProperty).Subscribe(_ =>
                {
                    if (!_subscribingVisibility)
                        RefreshAnimation();
                }));
            }
        }
        finally
        {
            _subscribingVisibility = false;
        }
    }

    private void ClearVisibilitySubscriptions()
    {
        if (_visibilitySubscriptions.Count == 0)
            return;
        foreach (var subscription in _visibilitySubscriptions)
            subscription.Dispose();
        _visibilitySubscriptions.Clear();
    }

    private void RefreshAnimation()
    {
        Foreground = MutedBrush;
        if (!IsLive || !this.IsAttachedToVisualTree())
        {
            ClearVisibilitySubscriptions();
            StopAnimation();
            return;
        }

        EnsureVisibilitySubscriptions();
        if (!IsVisible || this.GetVisualAncestors().Any(ancestor => !ancestor.IsVisible))
        {
            StopAnimation();
            return;
        }

        if (_maskBrush is null)
        {
            _maskBrush = new LinearGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Transparent, .18),
                    new GradientStop(Colors.White, .5),
                    new GradientStop(Colors.Transparent, .82),
                    new GradientStop(Colors.Transparent, 1)
                ]
            };
        }

        if (!_running)
        {
            _running = true;
            _started = Stopwatch.GetTimestamp();
            Register(this);
        }
        UpdateMask(Stopwatch.GetTimestamp());
    }

    internal void UpdateMask(long now)
    {
        if (_maskBrush is not { } mask)
            return;

        double textWidth = _renderedWidth > 1
            ? Math.Max(_renderedWidth, 64)
            : Math.Max(Bounds.Width - Padding.Left - Padding.Right, 64);
        double bandPixels = Math.Clamp(textWidth * 0.48, 48, 160);
        double band = bandPixels / textWidth;
        double phase = TestPhaseOverride ?? (Stopwatch.GetElapsedTime(_started, now).TotalSeconds % 1.6 / 1.6);
        double start = -band + phase * (1.0 + band);
        mask.StartPoint = new RelativePoint(start, 0, RelativeUnit.Relative);
        mask.EndPoint = new RelativePoint(start + band, 0, RelativeUnit.Relative);
        InvalidateVisual();
    }

    private void StopAnimation()
    {
        if (_running)
        {
            _running = false;
            Unregister(this);
        }
        _maskBrush = null;
        DisposeHighlightLayout();
        Foreground = MutedBrush;
        InvalidateVisual();
    }

    private static void Register(AgentShimmerTextBlock block)
    {
        s_active.Add(block);
        if (s_timer is null)
        {
            s_timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            s_timer.Tick += OnSharedTick;
            s_timer.Start();
        }
    }

    private static void Unregister(AgentShimmerTextBlock block)
    {
        s_active.Remove(block);
        if (s_active.Count == 0 && s_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnSharedTick;
            s_timer = null;
        }
    }

    private static void OnSharedTick(object? sender, EventArgs e)
    {
        if (s_active.Count == 0)
            return;
        long now = Stopwatch.GetTimestamp();
        foreach (var block in s_active.ToArray())
        {
            if (block.IsEffectivelyVisible)
                block.UpdateMask(now);
            else
                block.StopAnimation();
        }
    }
}
