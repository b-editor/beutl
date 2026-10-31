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
    public static readonly MediaTime DefaultDuration = MediaTime.FromSeconds(0.5);

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
    // of its own, only the span it adds on its side of the boundary. The span is laid out once, not per
    // frame, so the duration takes no expression.
    [Display(Name = nameof(GraphicsStrings.ClipTransition_Duration), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00", ParseLimitsInInvariantCulture = true)]
    public new IProperty<MediaTime> Duration { get; } = Property.CreateValue(DefaultDuration);

    [Display(Name = nameof(GraphicsStrings.ClipTransition_Easing), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Easing> Easing { get; } = Property.CreateExpression<Easing>(new LinearEasing());

    public partial class Resource
    {
        /// <summary>
        /// Draws one frame of the transition. A transition that does not draw its own, such as one whose
        /// type could not be loaded, cross-dissolves.
        /// </summary>
        public virtual void Draw(ClipTransitionContext context)
        {
            context.DrawCrossDissolve();
        }

        /// <summary>
        /// Brings the resource of a brush owned by this resource up to date. A brush the transition draws
        /// with belongs to its resource alone, so it is reconciled here rather than through a property; a
        /// change moves this resource's version so the previous frame's drawing is not reused.
        /// </summary>
        protected void Reconcile<TBrush, TResource>(TBrush brush, ref TResource? resource, CompositionContext context)
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
            bool versionBumped = false;
            resource.Reconcile(brush, context, ref versionBumped);
            if (resource.Version != version)
            {
                Version++;
            }
        }

        // Owns the changing outline used by geometric reveals. Each side is drawn just once, even when
        // the outline contains many tiles, and the resource version invalidates the previous frame's clip.
        // A reveal draws a settled side itself before building its outline.
        private protected sealed class GeometryMask : IDisposable
        {
            private SKPathGeometry.Resource? _geometry;

            public void Draw(ClipTransitionContext context, GeometryContext path)
            {
                _geometry ??= new SKPathGeometry().ToResource(CompositionContext.Default);
                _geometry.SetSKPath(path.NativeObject, clone: true);
                context.DrawClipped(_geometry);
            }

            public static void AddRectangle(IGeometryContext path, Rect rect)
            {
                if (rect.Width <= 0 || rect.Height <= 0) return;

                path.MoveTo(rect.TopLeft);
                path.LineTo(rect.TopRight);
                path.LineTo(rect.BottomRight);
                path.LineTo(rect.BottomLeft);
                path.Close();
            }

            public void Dispose()
            {
                _geometry?.Dispose();
                _geometry = null;
            }
        }

        // The colour a dip passes through, as a resource of a brush owned by the transition.
        private protected sealed class DipFill
        {
            private SolidColorBrush? _brush;
            private SolidColorBrush.Resource? _fill;

            public void Update(Resource owner, Media.Color color, CompositionContext context)
            {
                _brush ??= new SolidColorBrush();
                _brush.Color.Value = color;
                owner.Reconcile(_brush, ref _fill, context);
            }

            public void Draw(ClipTransitionContext context) => context.DrawThrough(_fill);

            public void Dispose()
            {
                _fill?.Dispose();
                _fill = null;
                _brush = null;
            }
        }
    }
}
