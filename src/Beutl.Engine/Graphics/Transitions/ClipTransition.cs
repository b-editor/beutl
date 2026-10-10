using System.ComponentModel.DataAnnotations;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Transitions;

/// <summary>
/// One side of a transition at a clip boundary: what an element contributes to the transition into it
/// (its enter transition) or out of it (its exit transition).
/// </summary>
/// <remarks>
/// The transition at the boundary between two adjacent elements on a layer spans the outgoing element's
/// exit <see cref="Duration"/> before the cut and the incoming element's enter <see cref="Duration"/> after
/// it. Throughout that span both elements are drawn, each holding its edge frame where it runs past its
/// own range, and blended the way the transition's type draws them. When both sides set a transition, the
/// incoming element's enter transition decides how they blend; the outgoing element's exit transition then
/// only contributes its duration. A side without an adjacent element blends with nothing, so it fades the
/// element in or out over whatever lies beneath the layer.
/// </remarks>
[Display(Name = nameof(GraphicsStrings.ClipTransition), ResourceType = typeof(GraphicsStrings))]
[FallbackType(typeof(FallbackClipTransition))]
public abstract partial class ClipTransition : EngineObject
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(0.5);

    protected ClipTransition()
    {
        ScanProperties<ClipTransition>();
    }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Graphics;

    /// <summary>
    /// Maps linear progress through the transition onto <paramref name="easing"/>, kept within [0, 1]: a
    /// transition cannot blend past either clip, so an easing that overshoots holds at the end it passes.
    /// </summary>
    public static float Ease(Easing? easing, float progress)
    {
        if (easing == null) return progress;

        float eased = easing.Ease(progress);
        return float.IsFinite(eased) ? Math.Clamp(eased, 0, 1) : progress;
    }

    // Hides EngineObject.Duration, the length of the object's time range: a transition has no time range
    // of its own, only the span it adds on its side of the boundary.
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Duration), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00", ParseLimitsInInvariantCulture = true)]
    public new IProperty<TimeSpan> Duration { get; } = Property.Create(DefaultDuration);

    [Display(Name = nameof(GraphicsStrings.ClipTransition_Easing), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Easing> Easing { get; } = Property.Create<Easing>(new LinearEasing());

    public partial class Resource
    {
        // Draws one frame of the transition. One that does not draw its own, such as a transition whose
        // type could not be loaded, cross-dissolves.
        internal virtual void Draw(TransitionDrawing drawing)
        {
            drawing.DrawCrossDissolve();
        }

        // A brush the transition draws with belongs to its resource alone, so it is reconciled here rather
        // than through a property; a change moves this resource's version so a cached recording is not
        // replayed.
        private protected void Reconcile<TBrush, TResource>(TBrush brush, ref TResource? resource, CompositionContext context)
            where TBrush : Brush
            where TResource : Brush.Resource
        {
            if (resource == null)
            {
                resource = (TResource)brush.ToResource(context);
                Version++;
                return;
            }

            int version = resource.Version;
            bool updateOnly = false;
            resource.Update(brush, context, ref updateOnly);
            if (resource.Version != version)
            {
                Version++;
            }
        }

        // The colour a dip passes through, as a resource of a brush owned by the transition.
        private protected void UpdateDipFill(
            ref SolidColorBrush? brush, ref SolidColorBrush.Resource? fill, Media.Color color, CompositionContext context)
        {
            brush ??= new SolidColorBrush();
            brush.Color.CurrentValue = color;
            Reconcile(brush, ref fill, context);
        }
    }
}
