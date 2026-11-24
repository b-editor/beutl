using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Puppet), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Puppet : FilterEffect, IEditingHandleProvider
{
    [ThreadStatic]
    private static PlacementCapture? s_placementCapture;

    private readonly EffectEditingFrames _editingFrames = new();

    public Puppet() => ScanProperties<Puppet>();

    [Display(Name = nameof(GraphicsStrings.Puppet_Pins), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<PuppetPin> Pins { get; } = Property.CreateList<PuppetPin>();

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time) => GetEditingHandles(time, default);

    public IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time, Rect fallbackBounds)
    {
        (Rect bounds, MediaTime evaluationTime) = GetEditingFrame(time, fallbackBounds);
        if (!EffectEditingFrames.HasArea(bounds)) return [];

        var evaluation = new EvaluationContext(evaluationTime);
        var handles = new List<EditingHandle>(Pins.Count);
        for (int i = 0; i < Pins.Count; i++)
        {
            PuppetPin pin = Pins[i];
            if (pin is null) continue;

            Point normalized = pin.Position.Evaluate(evaluation);
            var position = new Point(bounds.X + normalized.X * bounds.Width, bounds.Y + normalized.Y * bounds.Height);
            if (float.IsFinite(position.X) && float.IsFinite(position.Y))
                handles.Add(new EditingHandle(pin.Id.Value.ToString(), position, $"{GraphicsStrings.Puppet_Pin} {i + 1}"));
        }
        return handles;
    }

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time, Rect fallbackBounds)
    {
        (Rect bounds, MediaTime evaluationTime) = GetEditingFrame(time, fallbackBounds);
        return position =>
        {
            if (!EffectEditingFrames.HasArea(bounds) || !float.IsFinite(position.X) || !float.IsFinite(position.Y)
                || !Guid.TryParse(id, out Guid pinId)
                || Pins.FirstOrDefault(pin => pin?.Id.Value == pinId) is not { } pin)
                return;

            var normalized = new Point((position.X - bounds.X) / bounds.Width, (position.Y - bounds.Y) / bounds.Height);
            if (pin.Position.HasExpression || !float.IsFinite(normalized.X) || !float.IsFinite(normalized.Y)) return;

            if (pin.Position.GetPropertyAnimation() is ComponentAnimation<Point, float> components)
            {
                SetValueAt((IExpressionProperty<float>)components.Components[0], normalized.X, evaluationTime);
                SetValueAt((IExpressionProperty<float>)components.Components[1], normalized.Y, evaluationTime);
            }
            else
            {
                SetValueAt(pin.Position, normalized, evaluationTime);
            }
        };
    }

    public Action<Point> BeginEditingHandleDrag(string id, MediaTime time) => BeginEditingHandleDrag(id, time, default);

    public void MoveEditingHandle(string id, Point position, MediaTime time) => BeginEditingHandleDrag(id, time)(position);

    private static void SetValueAt<T>(IExpressionProperty<T> property, T value, MediaTime evaluationTime)
    {
        if (property.HasExpression) return;

        if (property.GetKeyFrameAnimation() is not KeyFrameAnimation<T> animation)
        {
            property.Value = value;
            return;
        }

        MediaTime localTime = animation.ToLocal(evaluationTime);
        var key = animation.KeyFrames.OfType<KeyFrame<T>>()
            .FirstOrDefault(key => key.KeyTime.Value == localTime);
        if (key is not null)
        {
            key.Value.Value = value;
        }
        else if (!EqualityComparer<T>.Default.Equals(property.Evaluate(new EvaluationContext(evaluationTime)), value))
        {
            animation.KeyFrames.Add(new KeyFrame<T>
            {
                Easing = { Value = new SplineEasing() },
                KeyTime = { Value = localTime },
                Value = { Value = value }
            }, out _);
        }
    }

    private (Rect Bounds, MediaTime Time) GetEditingFrame(MediaTime time, Rect fallbackBounds)
    {
        if (IsEnabled.Value && _editingFrames.TryGet(time, out var frame))
            return frame;
        return (fallbackBounds, time);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        _editingFrames.Clear();
    }

    // The point picker renders one isolated frame. Only the effect it is editing supplies a mesh to that request.
    internal PinPlacement? CapturePinPlacement(Action render)
    {
        var capture = new PlacementCapture(this);
        PlacementCapture? previous = s_placementCapture;
        s_placementCapture = capture;
        try
        {
            render();
            return capture.Placement;
        }
        finally
        {
            s_placementCapture = previous;
        }
    }

    private sealed class PlacementCapture(Puppet owner)
    {
        public Puppet Owner { get; } = owner;
        public PinPlacement? Placement { get; set; }
    }

    internal sealed class PinPlacement(Rect bounds, PuppetMesh mesh, Vector2[] deformed)
    {
        public PuppetPin? CreatePin(Point localPosition)
        {
            var point = new Vector2(localPosition.X - bounds.X, localPosition.Y - bounds.Y);
            if (!mesh.TryMapToRest(deformed, point, out Vector2 rest))
                return null;

            // Two pins on the same rest vertex cannot have independent destinations.
            foreach (int vertex in mesh.PinVertices)
            {
                if (vertex >= 0 && Vector2.DistanceSquared(rest, mesh.Rest[vertex]) < 0.25f)
                    return null;
            }

            Point destination = PuppetMesh.Normalize(bounds.Size, point);
            Point anchor = PuppetMesh.Normalize(bounds.Size, rest);
            anchor = new Point(Math.Clamp(anchor.X, 0, 1), Math.Clamp(anchor.Y, 0, 1));
            return new PuppetPin
            {
                RestPosition = { Value = anchor },
                Position = { Value = ReferenceEquals(mesh.Rest, deformed) ? anchor : destination }
            };
        }
    }
}
