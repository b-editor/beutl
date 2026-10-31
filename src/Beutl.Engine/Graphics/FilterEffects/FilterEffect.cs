using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[FallbackType(typeof(FallbackFilterEffect))]
[PresenterType(typeof(FilterEffectPresenter))]
public abstract partial class FilterEffect : EngineObject
{
    public abstract partial class Resource
    {
        /// <summary>Records this effect's operations from its evaluated values.</summary>
        /// <remarks>
        /// To apply another effect, such as a composite's child, call <see cref="FilterEffectContext.Apply"/>,
        /// which keeps continuation boundaries and the enabled state.
        /// </remarks>
        protected abstract void ApplyTo(FilterEffectContext context);

        // The engine records an effect only through FilterEffectContext.
        internal void Record(FilterEffectContext context) => ApplyTo(context);

        /// <summary>Whether this effect participates in source input evaluation before recording.</summary>
        public virtual bool UsesInputEvaluation => false;

        /// <summary>Prepares time samples of the input immediately before this effect.</summary>
        /// <returns>True to render through <see cref="RenderInput"/> instead of ApplyTo.</returns>
        /// <remarks>Runs after source reconciliation, even when current-time values did not change.</remarks>
        public virtual bool PrepareInput(FilterEffectInputContext context) => false;

        /// <summary>Records the replacement input in the drawable's parent coordinates.</summary>
        /// <remarks>The engine applies subsequent effects and the owner's blend mode once afterwards.</remarks>
        public virtual void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
            => input.Evaluate(input.EvaluationContext.Time).Draw(context);

        /// <summary>Whether this effect needs to control the remaining effects in its containing chain.</summary>
        /// <remarks>Such chains are recorded together so callbacks can retain and execute their suffix.</remarks>
        public virtual bool ControlsContinuation => false;

        /// <summary>Whether reevaluation at another time may read or replace the composition target.</summary>
        /// <remarks>
        /// Declare this independently of the evaluation time when a continuation can introduce an
        /// <see cref="CustomFilterEffectOptions.AccessesTarget"/> operation absent from the current recording.
        /// This requirement also applies while the current resource is disabled.
        /// </remarks>
        public virtual bool MayAccessTarget => false;

        internal CompositionContext? EvaluationContext { get; private set; }

        partial void PostReconcile(FilterEffect obj, CompositionContext context)
        {
            EvaluationContext = context.WithTime(context.Time);
        }

        /// <summary>
        /// Creates the render node for this effect. Override to supply a custom
        /// <see cref="FilterEffectRenderNode"/> subclass with a different working scale.
        /// </summary>
        public virtual FilterEffectRenderNode CreateRenderNode()
        {
            if (GetOriginal() is null)
            {
                throw new InvalidOperationException(
                    "The default FilterEffectRenderNode cannot be created from a detached filter-effect resource. "
                    + "Override CreateRenderNode() to provide a render node that supports detached resources.");
            }

            return new FilterEffectRenderNode(this);
        }

        public virtual PushedState Push(GraphicsContext2D context)
        {
            return context.PushNode(
                this,
                resource => resource.CreateRenderNode(),
                (node, resource) => node.Update(resource));
        }

        // GraphicsContext2D.PushFilterEffect records through this, so a group can give its children their own nodes
        // while its Push records one node.
        internal virtual PushedState PushInto(GraphicsContext2D context) => Push(context);
    }
}
