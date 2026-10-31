using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics;

public sealed partial class FallbackDrawable : Drawable, IFallback
{
    public new partial class Resource
    {
        // A drawable whose type did not load takes no space and draws nothing.
        protected override Size MeasureCore(Size availableSize) => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}

[FallbackType(typeof(FallbackDrawable))]
[PresenterType(typeof(DrawablePresenter))]
public abstract partial class Drawable : EngineObject
{
    public Drawable()
    {
        ScanProperties<Drawable>();
        FilterEffect.Value = new FilterEffectGroup();
        Transform.Value = new TransformGroup();
    }

    public override CompositionTarget GetCompositionTarget() => CompositionTarget.Graphics;

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Transform))]
    public IExpressionProperty<Transform?> Transform { get; } = Property.CreateExpression<Transform?>();

    [Display(Name = nameof(GraphicsStrings.Drawable_AlignmentX), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Transform))]
    public IExpressionProperty<AlignmentX> AlignmentX { get; } = Property.CreateAnimatable(Media.AlignmentX.Center);

    [Display(Name = nameof(GraphicsStrings.Drawable_AlignmentY), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Transform))]
    public IExpressionProperty<AlignmentY> AlignmentY { get; } = Property.CreateAnimatable(Media.AlignmentY.Center);

    [Display(Name = nameof(GraphicsStrings.TransformOrigin), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Transform))]
    public IExpressionProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);

    [Display(Name = nameof(GraphicsStrings.FilterEffect), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.FilterEffect))]
    public IExpressionProperty<FilterEffect?> FilterEffect { get; } = Property.CreateExpression<FilterEffect?>();

    [Display(Name = nameof(GraphicsStrings.Drawable_BlendMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<BlendMode> BlendMode { get; } = Property.CreateAnimatable(Graphics.BlendMode.SrcOver);

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    public abstract partial class Resource
    {
        private FilterEffectInputContext? _input;
        private BlendMode? _sampleBlendMode;

        // Input sampling applies the owner's blend once after combining samples, but containers still
        // use its evaluated mode to decide whether their children need an isolated surface.
        internal BlendMode EvaluatedBlendMode => _sampleBlendMode ?? BlendMode;

        // Operations outside the drawable's own effect chain survive that chain's input isolation.
        internal virtual bool ClearsTargetBeforeEffects => false;

        internal void SuppressInputBlendMode()
        {
            _sampleBlendMode = BlendMode;
            BlendMode = BlendMode.SrcOver;
        }

        internal IReadOnlyList<FilterEffectInputChain.Entry>? EvaluatedInputChain { get; set; }

        /// <summary>Completes source-dependent effects after derived properties and consumed flow are ready.</summary>
        /// <remarks>Generated resources call this automatically. Hand-written resources call it last in Reconcile.</remarks>
        protected void CompleteReconciliation(CompositionContext context)
        {
            if (ReferenceEquals(context.SampledDrawable, GetOriginal())) return;
            PrepareFilterEffectInput(context, null);
        }

        // The drawable an input sample evaluates holds only the effects ahead of the sampling one.
        internal override bool TryReconcileSampledChild(IProperty property, CompositionContext context,
            ref EngineObject.Resource? field)
        {
            if (context.SampledDrawable is not { } sampledDrawable
                || !ReferenceEquals(sampledDrawable, GetOriginal())
                || !ReferenceEquals(property, sampledDrawable.FilterEffect))
            {
                return false;
            }

            var prefix = FilterEffectInputChain.EvaluatePrefix(this, context.Get(sampledDrawable.FilterEffect), context, field);
            if (!ReferenceEquals(field, prefix)) field?.Dispose();
            field = prefix;
            return true;
        }

        internal void PrepareFilterEffectInput(CompositionContext context,
            IReadOnlyList<FilterEffectInputChain.Entry>? chain)
        {
            bool evaluatesInput = IsEnabled && FilterEffectInputChain.HasInputEvaluation(FilterEffect);
            if (_input == null && !evaluatesInput) return;
            // The previous samples are reevaluated in place where the new ones claim them; failures discard them.
            var recycled = new List<FilterEffectInputSlot>();
            if (_input != null)
            {
                _input.Recycle(recycled);
                _input = null;
                Version++;
            }
            try
            {
                if (!evaluatesInput) return;
                chain ??= FilterEffectInputChain.Capture(FilterEffect);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    if (!chain[i].Enabled || !chain[i].Resource.UsesInputEvaluation) continue;
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var input = new FilterEffectInputContext(this, context, chain, i, recycled);
                    try
                    {
                        if (chain[i].Resource.PrepareInput(input))
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();
                            _input = input;
                            context.ContainsScene3D |= input.EvaluationContext.ContainsScene3D;
                            // Sampled properties can change while every current-time property stays unchanged.
                            Version++;
                            return;
                        }
                    }
                    catch
                    {
                        input.Dispose();
                        Version++;
                        throw;
                    }
                    input.Recycle(recycled);
                }
            }
            finally
            {
                // Slots no new sample claimed: fewer samples, a removed effect, or a failed preparation.
                FilterEffectInputSlot.DisposeAll(recycled);
            }
        }

        /// <summary>Measures this evaluated drawable's content without drawing it.</summary>
        public Size Measure(Size availableSize) => MeasureCore(availableSize);

        /// <summary>Records this evaluated drawable, including effects that replace its time-sampled input.</summary>
        public void Render(GraphicsContext2D context)
        {
            if (!IsEnabled) return;
            if (_input != null) _input.Render(context);
            else RenderCore(context);
        }

        /// <summary>Measures this drawable's content from its evaluated values.</summary>
        protected abstract Size MeasureCore(Size availableSize);

        /// <summary>Draws this drawable's content in its own coordinates.</summary>
        protected abstract void OnDraw(GraphicsContext2D context);

        /// <summary>Records this drawable with its blend mode, transform, opacity and filter effect.</summary>
        /// <remarks>
        /// <see cref="Render(GraphicsContext2D)"/> calls this unless an effect replaces the time-sampled input.
        /// Override to change how the content is placed or composited.
        /// </remarks>
        protected virtual void RenderCore(GraphicsContext2D context)
        {
            Size availableSize = context.Size;
            Size size = MeasureCore(availableSize);

            Matrix transform = GetTransformMatrix(availableSize, size);
            using (context.PushBlendMode(BlendMode))
            using (context.PushTransform(transform))
            using (context.PushOpacity(Opacity / 100f))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            {
                OnDraw(context);
            }
        }

        internal Matrix GetTransformMatrix(Size availableSize, Size coreBounds)
        {
            Vector pt = CalculateAlignmentTranslate(AlignmentX, AlignmentY, coreBounds, availableSize);
            var origin = TransformOrigin.ToPixels(coreBounds);
            Matrix offset = Matrix.CreateTranslation(origin);

            if (Transform != null)
            {
                Matrix transform = Transform.Matrix;
                return (-offset) * transform * offset * Matrix.CreateTranslation(pt);
            }
            else
            {
                return Matrix.CreateTranslation(pt);
            }
        }

        partial void PreDispose(bool disposing)
        {
            _input?.Dispose();
            _input = null;
        }
    }


    /// <summary>
    /// Places <paramref name="bounds"/> inside <paramref name="canvasSize"/> by the given alignment; an axis whose
    /// canvas extent is unbounded stays at zero.
    /// </summary>
    internal static Point CalculateAlignmentTranslate(
        Media.AlignmentX alignmentX,
        Media.AlignmentY alignmentY,
        Size bounds,
        Size canvasSize)
    {
        float x = 0;
        float y = 0;

        if (float.IsFinite(canvasSize.Width))
        {
            switch (alignmentX)
            {
                case Media.AlignmentX.Left:
                    x = 0;
                    break;
                case Media.AlignmentX.Center:
                    x = canvasSize.Width / 2 - bounds.Width / 2;
                    break;
                case Media.AlignmentX.Right:
                    x = canvasSize.Width - bounds.Width;
                    break;
            }
        }

        if (float.IsFinite(canvasSize.Height))
        {
            switch (alignmentY)
            {
                case Media.AlignmentY.Top:
                    y = 0;
                    break;
                case Media.AlignmentY.Center:
                    y = canvasSize.Height / 2 - bounds.Height / 2;
                    break;
                case Media.AlignmentY.Bottom:
                    y = canvasSize.Height - bounds.Height;
                    break;
            }
        }

        return new Point(x, y);
    }
}
