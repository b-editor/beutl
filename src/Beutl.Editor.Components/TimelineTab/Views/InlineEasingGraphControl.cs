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

        CompositeDisposable itemSubscriptions = [];
        itemSubscriptions.DisposeWith(_subscriptions);

        viewModel.Items.ForEachItem(
            (idx, item) =>
            {
                item.Left.Subscribe(_ => InvalidateVisual()).DisposeWith(itemSubscriptions);
                item.Model.Edited += OnKeyFrameEdited;
                InvalidateVisual();
            },
            (idx, item) =>
            {
                item.Model.Edited -= OnKeyFrameEdited;
                // itemSubscriptions全体を再構築する代わりに、InvalidateVisualだけ呼ぶ
                InvalidateVisual();
            },
            () =>
            {
                itemSubscriptions.Clear();
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

        double width = Bounds.Width;
        double height = Bounds.Height;
        double usableHeight = height - VerticalPadding * 2;
        if (usableHeight <= 0) return;

        _pen.Brush = Brush;

        // Build a single StreamGeometry with one figure per visible segment, then draw once.
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i < sorted.Length - 1; i++)
            {
                double left = sorted[i].left;
                double right = sorted[i + 1].left;
                double segmentWidth = right - left;

                if (segmentWidth < 2) continue;

                // Skip segments fully outside the visible bounds.
                if (right < 0 || left > width) continue;

                Easing easing = sorted[i + 1].model.Easing;
                int comparison = Compare(sorted[i].model.Value, sorted[i + 1].model.Value);

                int sampleCount = Math.Clamp((int)segmentWidth, 2, 100);

                float e0 = Math.Clamp(easing.Ease(0f), 0f, 1f);
                ctx.BeginFigure(new Point(left, GetY(comparison, e0, height, usableHeight)), false);

                for (int j = 1; j <= sampleCount; j++)
                {
                    float t = j / (float)sampleCount;
                    float e = Math.Clamp(easing.Ease(t), 0f, 1f);
                    double x = left + t * segmentWidth;
                    ctx.LineTo(new Point(x, GetY(comparison, e, height, usableHeight)));
                }

                ctx.EndFigure(false);
            }
        }

        context.DrawGeometry(null, _pen, geometry);
    }

    private static double GetY(int comparison, float e, double height, double usableHeight)
    {
        if (comparison > 0)
            return VerticalPadding + e * usableHeight;
        else if (comparison < 0)
            return height - VerticalPadding - e * usableHeight;
        else
            return height / 2;
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
