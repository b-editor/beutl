using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed class InlineEasingGraphControl : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<InlineEasingGraphControl, IBrush?>(nameof(Brush), Brushes.White);

    private const double VerticalPadding = 1.0;

    private readonly Pen _pen = new()
    {
        LineJoin = PenLineJoin.Round,
        LineCap = PenLineCap.Round,
        Thickness = 1.5,
    };

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
            SubscribeToItems(viewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private void SubscribeToItems(InlineAnimationLayerViewModel viewModel)
    {
        if (_subscriptions == null) return;

        var perItemDisposables = new Dictionary<InlineKeyFrameViewModel, IDisposable>();

        Disposable.Create(() =>
        {
            foreach (var d in perItemDisposables.Values) d.Dispose();
            perItemDisposables.Clear();
        }).DisposeWith(_subscriptions);

        viewModel.Items.ForEachItem(
            (idx, item) =>
            {
                var d = new CompositeDisposable();
                item.Left.Subscribe(_ => InvalidateVisual()).DisposeWith(d);
                item.Model.Edited += OnKeyFrameEdited;
                Disposable.Create(() => item.Model.Edited -= OnKeyFrameEdited).DisposeWith(d);
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
            .DisposeWith(_subscriptions);
    }

    private void OnKeyFrameEdited(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        CoreList<InlineKeyFrameViewModel> items = viewModel.Items;
        if (items.Count < 2) return;

        // Leftでソートしたスナップショットを作成
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

            Easing easing = sorted[i + 1].model.Easing;
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
            try
            {
                return comparable.CompareTo(nextValue);
            }
            catch
            {
                // 型が異なる場合など
            }
        }

        return 1;
    }
}
