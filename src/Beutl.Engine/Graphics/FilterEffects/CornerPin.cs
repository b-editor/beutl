using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.JsonConverters;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.CornerPin), ResourceType = typeof(GraphicsStrings))]
public sealed partial class CornerPin : FilterEffect, IEditingStageProvider
{
    private static readonly Corners<RelativePoint> s_defaultCorners = new(
        RelativePoint.TopLeft, new RelativePoint(1, 0, RelativeUnit.Relative),
        RelativePoint.BottomRight, new RelativePoint(0, 1, RelativeUnit.Relative));

    private readonly EffectEditingFrames _editingFrames = new();

    public CornerPin() => ScanProperties<CornerPin>();

    [Display(Name = nameof(GraphicsStrings.CornerPin_TopLeft), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> TopLeft { get; } = Property.CreateAnimatable(s_defaultCorners.TopLeft);

    [Display(Name = nameof(GraphicsStrings.CornerPin_TopRight), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> TopRight { get; } = Property.CreateAnimatable(s_defaultCorners.TopRight);

    [Display(Name = nameof(GraphicsStrings.CornerPin_BottomRight), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> BottomRight { get; } = Property.CreateAnimatable(s_defaultCorners.BottomRight);

    [Display(Name = nameof(GraphicsStrings.CornerPin_BottomLeft), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<RelativePoint> BottomLeft { get; } = Property.CreateAnimatable(s_defaultCorners.BottomLeft);

    string IEditingStageProvider.EditingStageHint => GraphicsStrings.CornerPin_EditingPreviewHint;

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time) => GetEditingHandles(time, default);

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time, Rect fallbackBounds)
    {
        (Rect bounds, MediaTime evaluationTime) = GetEditingFrame(time, fallbackBounds);
        if (!EffectEditingFrames.HasArea(bounds)) return [];

        Corners<Point> corners = EvaluateCorners(bounds, evaluationTime);
        var handles = new List<EditingHandle>(4);
        AddHandle("top-left", corners.TopLeft, GraphicsStrings.CornerPin_TopLeft);
        AddHandle("top-right", corners.TopRight, GraphicsStrings.CornerPin_TopRight);
        AddHandle("bottom-right", corners.BottomRight, GraphicsStrings.CornerPin_BottomRight);
        AddHandle("bottom-left", corners.BottomLeft, GraphicsStrings.CornerPin_BottomLeft);
        return handles;

        void AddHandle(string id, Point position, string label)
        {
            if (IsFinite(position))
                handles.Add(new EditingHandle(id, position, label));
        }
    }

    public IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time) => GetEditingGuides(time, default);

    public IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time, Rect fallbackBounds)
    {
        (Rect bounds, MediaTime evaluationTime) = GetEditingFrame(time, fallbackBounds);
        if (!EffectEditingFrames.HasArea(bounds)) return [];

        Corners<Point> corners = EvaluateCorners(bounds, evaluationTime);
        var guides = new List<EditingGuideLine>(4);
        AddGuide(corners.TopLeft, corners.TopRight);
        AddGuide(corners.TopRight, corners.BottomRight);
        AddGuide(corners.BottomRight, corners.BottomLeft);
        AddGuide(corners.BottomLeft, corners.TopLeft);
        return guides;

        void AddGuide(Point start, Point end)
        {
            if (IsFinite(start) && IsFinite(end))
                guides.Add(new EditingGuideLine(start, end));
        }
    }

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time) => BeginEditingHandleDrag(id, time, default);

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time, Rect fallbackBounds)
    {
        IExpressionProperty<RelativePoint>? property = id switch
        {
            "top-left" => TopLeft,
            "top-right" => TopRight,
            "bottom-right" => BottomRight,
            "bottom-left" => BottomLeft,
            _ => null,
        };
        (Rect bounds, MediaTime evaluationTime) = GetEditingFrame(time, fallbackBounds);
        if (property is null || property.HasExpression || !EffectEditingFrames.HasArea(bounds))
            return static _ => { };

        var evaluation = new EvaluationContext(evaluationTime);
        RelativeUnit unit = property.Evaluate(evaluation).Unit;
        Rational rate = Project.ReadFrameRate(this.FindHierarchicalParent<Project>());
        return position =>
        {
            if (property.HasExpression) return;

            Point offset = position - bounds.Position;
            Point value = unit == RelativeUnit.Absolute
                ? offset
                : new Point(offset.X / bounds.Width, offset.Y / bounds.Height);
            if (!IsFinite(value)) return;

            if (property.GetPropertyAnimation() is ComponentAnimation<RelativePoint, float> components)
            {
                SetValueAt((IExpressionProperty<float>)components.Components[0], value.X, evaluation, rate);
                SetValueAt((IExpressionProperty<float>)components.Components[1], value.Y, evaluation, rate);
            }
            else
            {
                SetValueAt(property, new RelativePoint(value, unit), evaluation, rate);
            }
        };
    }

    public void MoveEditingHandle(string id, Point position, MediaTime time) => BeginEditingHandleDrag(id, time)(position);

    private static void SetValueAt<T>(IExpressionProperty<T> property, T value, EvaluationContext evaluation, Rational rate)
    {
        if (property.HasExpression) return;

        if (property.GetKeyFrameAnimation() is not { } animation)
        {
            property.Value = value;
            return;
        }

        MediaTime localTime = animation.ToLocal(evaluation.Time).RoundToFrame(rate, MidpointRounding.AwayFromZero);
        var key = animation.KeyFrames.OfType<KeyFrame<T>>()
            .FirstOrDefault(key => MediaTimeJsonConverter.SnapToFrame(key.KeyTime.Value, rate) == localTime);
        if (key is not null)
        {
            key.Value.Value = value;
        }
        else if (!EqualityComparer<T>.Default.Equals(property.Evaluate(evaluation), value))
        {
            animation.KeyFrames.Add(new KeyFrame<T>
            {
                Easing = { Value = new SplineEasing() },
                KeyTime = { Value = localTime },
                Value = { Value = value },
            }, out _);
        }
    }

    private Corners<Point> EvaluateCorners(Rect bounds, MediaTime time)
    {
        var evaluation = new EvaluationContext(time);
        return ResolveCorners(bounds, new Corners<RelativePoint>(TopLeft.Evaluate(evaluation), TopRight.Evaluate(evaluation),
            BottomRight.Evaluate(evaluation), BottomLeft.Evaluate(evaluation)));
    }

    private (Rect Bounds, MediaTime Time) GetEditingFrame(MediaTime time, Rect fallbackBounds)
    {
        if (IsEnabled.Value && _editingFrames.TryGet(time, out var frame))
            return frame;
        if (EffectEditingFrames.HasArea(fallbackBounds))
            return (fallbackBounds, time);

        if (this.FindHierarchicalParent<Drawable>() is not { } drawable) return (Rect.Empty, time);

        using Drawable.Resource resource = drawable.ToResource(new CompositionContext(time));
        return (new Rect(resource.Measure(Size.Infinity)), time);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        _editingFrames.Clear();
    }

    private readonly record struct Corners<T>(T TopLeft, T TopRight, T BottomRight, T BottomLeft);
}
