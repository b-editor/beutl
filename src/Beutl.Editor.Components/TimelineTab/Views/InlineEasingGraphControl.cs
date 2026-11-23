using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

// Draws an inline layer's animation behind its keys. A value the graph editor plots is drawn as it changes over the
// clip, one line for each of its channels, between the lowest and the highest value its keys and their easings reach;
// before the first key and after the last the value holds. Any other value shows the shape of each easing, from the
// bottom to the top or the top to the bottom as the value rises or falls.
public sealed class InlineEasingGraphControl : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<InlineEasingGraphControl, IBrush?>(nameof(Brush), Brushes.White);

    private const double VerticalPadding = 1.0;

    // Keeps a value line off the edges of the row, where it would read as the row's border.
    private const double ValuePadding = 3.0;

    // A segment between two keys takes a sample every two pixels, up to this many.
    private const int MaxSamplesPerSegment = 64;

    // The progress of a sample at a key's own position that reads the value the segment had up to it.
    private const float JustBeforeKey = 1f - 1e-6f;

    private static readonly Dictionary<Color, IBrush> s_channelBrushes = [];

    private readonly Pen _pen = new()
    {
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 1.5,
    };

    private readonly Dictionary<IBrush, Pen> _channelPens = [];
    private CompositeDisposable? _subscriptions;

    static InlineEasingGraphControl()
    {
        AffectsRender<InlineEasingGraphControl>(BrushProperty);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _subscriptions?.Dispose();
        _subscriptions = null;

        if (DataContext is InlineAnimationLayerViewModel viewModel)
        {
            _subscriptions = [];
            SubscribeToItems(viewModel, _subscriptions);
            // The value holds over the rest of the clip, wherever the clip and the animation's origin are.
            viewModel.LeftMargin.Subscribe(_ => InvalidateVisual()).DisposeWith(_subscriptions);
            viewModel.Element.BorderMargin.Subscribe(_ => InvalidateVisual()).DisposeWith(_subscriptions);
            viewModel.Element.Width.Subscribe(_ => InvalidateVisual()).DisposeWith(_subscriptions);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private void SubscribeToItems(InlineAnimationLayerViewModel viewModel, CompositeDisposable subscriptions)
    {
        var perItemDisposables = new Dictionary<InlineKeyFrameViewModel, IDisposable>();

        Disposable.Create(() =>
        {
            foreach (var d in perItemDisposables.Values) d.Dispose();
            perItemDisposables.Clear();
        }).DisposeWith(subscriptions);

        viewModel.Items.ForEachItem(
            (idx, item) =>
            {
                var d = new CompositeDisposable();
                item.Left.Subscribe(_ => InvalidateVisual()).DisposeWith(d);
                item.Model.Edited += OnKeyFrameEdited;
                Disposable.Create(() => item.Model.Edited -= OnKeyFrameEdited).DisposeWith(d);
                // The graph editor moves the control points of an easing without replacing it.
                item.Model.Easing.GetObservable()
                    .Select(easing => easing is SplineEasing spline
                        ? Observable.FromEventPattern(h => spline.Changed += h, h => spline.Changed -= h)
                        : Observable.Never<System.Reactive.EventPattern<object>>())
                    .Switch()
                    .Subscribe(_ => InvalidateVisual())
                    .DisposeWith(d);
                perItemDisposables[item] = d;
                InvalidateVisual();
            },
            (idx, item) =>
            {
                if (perItemDisposables.TryGetValue(item, out var d))
                {
                    d.Dispose();
                    perItemDisposables.Remove(item);
                }
                InvalidateVisual();
            },
            () =>
            {
                foreach (var d in perItemDisposables.Values) d.Dispose();
                perItemDisposables.Clear();
                InvalidateVisual();
            })
            .DisposeWith(subscriptions);
    }

    private void OnKeyFrameEdited(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        if (viewModel.Property.Animation is IKeyFrameAnimation animation
            && GraphEditorViewViewModelFactory.GetFactory(animation) is { ChannelReaders: { Count: > 0 } } factory)
        {
            RenderValues(context, viewModel, animation, factory);
        }
        else
        {
            RenderEasings(context, viewModel);
        }
    }

    private void RenderValues(DrawingContext context, InlineAnimationLayerViewModel viewModel, IKeyFrameAnimation animation,
        GraphEditorViewViewModelFactory factory)
    {
        CoreList<InlineKeyFrameViewModel> items = viewModel.Items;
        if (items.Count == 0) return;

        var keys = new (double X, IKeyFrame Model)[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            keys[i] = (items[i].Left.Value, items[i].Model);
        }

        // By where they are drawn, which a drag moves before the keys themselves.
        Array.Sort(keys, static (a, b) => a.X.CompareTo(b.X));
        double clipLeft = viewModel.Element.BorderMargin.Value.Left - viewModel.LeftMargin.Value.Left;
        double clipRight = clipLeft + viewModel.Element.Width.Value;
        IReadOnlyList<Func<object?, double>> readers = factory.ChannelReaders!;
        ValueLine[] lines = ValueLine.Sample(animation, keys, clipLeft, clipRight, readers);

        if (!ValueLine.TryGetRange(lines, out double min, out double max)) return;

        double top = ValuePadding;
        double usable = Bounds.Height - ValuePadding * 2;
        if (usable <= 0) return;

        IReadOnlyList<string> names = factory.ChannelNames;
        for (int channel = 0; channel < lines.Length; channel++)
        {
            ValueLine line = lines[channel];
            var geometry = new StreamGeometry();
            using (StreamGeometryContext figures = geometry.Open())
            {
                bool open = false;
                for (int i = 0; i < line.X.Length; i++)
                {
                    double value = line.Values[i];
                    if (!double.IsFinite(value))
                    {
                        if (open) figures.EndFigure(false);
                        open = false;
                        continue;
                    }

                    var point = new Point(line.X[i], top + ValueLine.ToHeight(value, min, max) * usable);
                    if (open)
                    {
                        figures.LineTo(point);
                    }
                    else
                    {
                        figures.BeginFigure(point, false);
                        open = true;
                    }
                }

                if (open) figures.EndFigure(false);
            }

            context.DrawGeometry(null, GetChannelPen(channel < names.Count ? names[channel] : "Self", lines.Length), geometry);
        }
    }

    // A value with one channel, and a color's alpha, take the row's foreground; the other channels take the colors the
    // graph editor draws them in.
    private Pen GetChannelPen(string name, int channelCount)
    {
        IBrush? brush = channelCount == 1 || name is "Self" or "Alpha"
            ? Brush
            : GetChannelBrush(GraphEditorViewViewModel.GetChannelColor(name));
        if (brush is null)
        {
            _pen.Brush = null;
            return _pen;
        }

        if (!_channelPens.TryGetValue(brush, out Pen? pen))
        {
            pen = new Pen(brush, _pen.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            _channelPens[brush] = pen;
        }

        return pen;
    }

    private static IBrush GetChannelBrush(Color color)
    {
        if (!s_channelBrushes.TryGetValue(color, out IBrush? brush))
        {
            brush = new ImmutableSolidColorBrush(color);
            s_channelBrushes[color] = brush;
        }

        return brush;
    }

    private void RenderEasings(DrawingContext context, InlineAnimationLayerViewModel viewModel)
    {
        CoreList<InlineKeyFrameViewModel> items = viewModel.Items;
        if (items.Count < 2) return;

        var sorted = new (double left, IKeyFrame model)[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            sorted[i] = (items[i].Left.Value, items[i].Model);
        }
        Array.Sort(sorted, static (a, b) => a.left.CompareTo(b.left));

        double height = Bounds.Height;
        double usableHeight = height - VerticalPadding * 2;
        if (usableHeight <= 0) return;

        _pen.Brush = Brush;

        for (int i = 0; i < sorted.Length - 1; i++)
        {
            double left = sorted[i].left;
            double right = sorted[i + 1].left;
            double segmentWidth = right - left;

            if (segmentWidth < 2) continue;

            Easing easing = sorted[i + 1].model.Easing.Value;
            int comparison = Compare(sorted[i].model.Value, sorted[i + 1].model.Value);

            int sampleCount = Math.Clamp((int)segmentWidth, 2, 100);

            for (int j = 0; j < sampleCount; j++)
            {
                float t1 = j / (float)sampleCount;
                float t2 = (j + 1) / (float)sampleCount;

                float e1 = Math.Clamp(easing.Ease(t1), 0f, 1f);
                float e2 = Math.Clamp(easing.Ease(t2), 0f, 1f);

                double x1 = left + t1 * segmentWidth;
                double x2 = left + t2 * segmentWidth;

                double y1, y2;
                if (comparison > 0)
                {
                    y1 = VerticalPadding + e1 * usableHeight;
                    y2 = VerticalPadding + e2 * usableHeight;
                }
                else if (comparison < 0)
                {
                    y1 = height - VerticalPadding - e1 * usableHeight;
                    y2 = height - VerticalPadding - e2 * usableHeight;
                }
                else
                {
                    y1 = height / 2;
                    y2 = height / 2;
                }

                context.DrawLine(_pen, new Point(x1, y1), new Point(x2, y2));
            }
        }
    }

    private static int Compare(object? prevValue, object? nextValue)
    {
        if (prevValue is IComparable comparable && nextValue != null)
        {
            return comparable.CompareTo(nextValue);
        }

        return 1;
    }

    // One channel of the value at each sample, from the left of the row.
    internal sealed class ValueLine(double[] x, double[] values)
    {
        public double[] X { get; } = x;

        public double[] Values { get; } = values;

        // Samples the value at each key, every two pixels between two keys, and at the clip's edges outside them. keys
        // are where the keys are drawn, from left to right.
        public static ValueLine[] Sample(IKeyFrameAnimation animation, IReadOnlyList<(double X, IKeyFrame Model)> keys,
            double clipLeft, double clipRight, IReadOnlyList<Func<object?, double>> readers)
        {
            var x = new List<double>();
            var values = new List<object?>();
            KeyValues sampler = KeyValues.For(animation.ValueType);
            (double firstX, IKeyFrame first) = keys[0];
            if (clipLeft < firstX)
            {
                x.Add(clipLeft);
                values.Add(sampler.ValueOf(first));
            }

            x.Add(firstX);
            values.Add(sampler.ValueOf(first));
            for (int i = 1; i < keys.Count; i++)
            {
                (double from, IKeyFrame previous) = keys[i - 1];
                (double to, IKeyFrame next) = keys[i];
                double width = to - from;
                int count = Math.Clamp((int)(width / 2), 1, MaxSamplesPerSegment);
                for (int j = 1; j <= count; j++)
                {
                    float progress = j / (float)count;
                    if (j == count)
                    {
                        // The value just before the key, so a value that holds until the key jumps there rather than
                        // ramping over the last sample.
                        x.Add(to);
                        values.Add(sampler.ValueAt(animation, previous, next, JustBeforeKey));
                    }

                    x.Add(from + width * progress);
                    values.Add(sampler.ValueAt(animation, previous, next, progress));
                }
            }

            (double lastX, IKeyFrame last) = keys[^1];
            if (clipRight > lastX)
            {
                x.Add(clipRight);
                values.Add(sampler.ValueOf(last));
            }

            double[] positions = [.. x];
            var lines = new ValueLine[readers.Count];
            for (int channel = 0; channel < lines.Length; channel++)
            {
                Func<object?, double> read = readers[channel];
                var channelValues = new double[values.Count];
                for (int i = 0; i < channelValues.Length; i++)
                {
                    channelValues[i] = read(values[i]);
                }

                lines[channel] = new ValueLine(positions, channelValues);
            }

            return lines;
        }

        // The lowest and the highest value of every channel, false when there is none.
        public static bool TryGetRange(IReadOnlyList<ValueLine> lines, out double min, out double max)
        {
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (ValueLine line in lines)
            {
                foreach (double value in line.Values)
                {
                    if (!double.IsFinite(value)) continue;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            return min <= max;
        }

        // Where value lies from the top of the drawn height, 0 at the top and 1 at the bottom, or halfway when every
        // value is the same.
        public static double ToHeight(double value, double min, double max)
            => max - min < 1e-9 ? 0.5 : (max - value) / (max - min);
    }

    // The values a key frame animation of some value type gives, as objects.
    private abstract class KeyValues
    {
        private static readonly Dictionary<Type, KeyValues> s_byType = [];

        public static KeyValues For(Type type)
        {
            if (!s_byType.TryGetValue(type, out KeyValues? values))
            {
                values = (KeyValues)Activator.CreateInstance(typeof(KeyValues<>).MakeGenericType(type))!;
                s_byType[type] = values;
            }

            return values;
        }

        public abstract object? ValueOf(IKeyFrame key);

        // The value at progress, from 0 at previous to 1 at next, as the animation interpolates between them.
        public abstract object? ValueAt(IKeyFrameAnimation animation, IKeyFrame previous, IKeyFrame next, float progress);
    }

    private sealed class KeyValues<T> : KeyValues
    {
        public override object? ValueOf(IKeyFrame key) => key is KeyFrame<T> typed ? typed.Value.Value : null;

        public override object? ValueAt(IKeyFrameAnimation animation, IKeyFrame previous, IKeyFrame next, float progress)
        {
            if (animation is not KeyFrameAnimation<T> typed
                || previous is not KeyFrame<T> from
                || next is not KeyFrame<T> to)
            {
                return null;
            }

            T? fromValue = from.Value.Value;
            T? toValue = to.Value.Value;
            if (fromValue is null) return toValue;
            if (toValue is null) return fromValue;

            MediaTime fromTime = from.KeyTime.Value;
            MediaTime toTime = to.KeyTime.Value;
            var time = new MediaTime(fromTime.Flicks + (long)((toTime.Flicks - fromTime.Flicks) * (double)progress));
            return typed.InterpolateValues(to.Easing.Value.Ease(progress), fromValue, toValue, fromTime, toTime, time);
        }
    }
}
