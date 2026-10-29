using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Group), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilterEffectGroup : FilterEffect
{
    public FilterEffectGroup()
    {
        ScanProperties<FilterEffectGroup>();
    }

    public IListProperty<FilterEffect> Children { get; } = Property.CreateList<FilterEffect>();

    public partial class Resource
    {
        private ContinuationSuffix? _continuationSuffix;

        public override bool ControlsContinuation
            => Children.Any(static child => child.IsEnabled && child.ControlsContinuation);

        public override bool MayAccessTarget => Children.Any(static child => child.MayAccessTarget);

        /// <summary>
        /// The children from the first continuation-controlling one onwards, when earlier children precede it.
        /// </summary>
        /// <remarks>
        /// The earlier children keep their own render nodes, such as a node graph effect that cannot be recorded
        /// by <see cref="FilterEffectContext"/>; only the continuation and the effects it controls share one node.
        /// </remarks>
        internal FilterEffect.Resource? ContinuationSuffixResource => _continuationSuffix;

        internal int ContinuationStart => _continuationSuffix?.Start ?? 0;

        protected override void ApplyTo(FilterEffectContext context)
        {
            ApplyChildren(context, 0);
        }

        private void ApplyChildren(FilterEffectContext context, int start)
        {
            context.ApplyTransactional(() =>
            {
                for (int i = start; i < Children.Count; i++)
                {
                    context.Apply(Children[i]);
                }
            });
        }

        partial void PostReconcile(FilterEffectGroup obj, CompositionContext context)
        {
            int start = Children.FindIndex(static child => child.IsEnabled && child.ControlsContinuation);
            if (start <= 0)
            {
                _continuationSuffix?.Dispose();
                _continuationSuffix = null;
                return;
            }

            _continuationSuffix ??= new ContinuationSuffix(this);
            _continuationSuffix.Reconcile(obj, context, start);
        }

        partial void PostDispose(bool disposing)
        {
            _continuationSuffix?.Dispose();
            _continuationSuffix = null;
        }

        // Children are owned by the group; the suffix only reads them and is reused across frames like its node.
        private sealed class ContinuationSuffix(Resource owner) : FilterEffect.Resource
        {
            private int _ownerVersion = -1;

            public int Start { get; private set; }

            public override bool ControlsContinuation => true;

            public override bool MayAccessTarget
            {
                get
                {
                    for (int i = Start; i < owner.Children.Count; i++)
                    {
                        if (owner.Children[i].MayAccessTarget) return true;
                    }

                    return false;
                }
            }

            public void Reconcile(FilterEffectGroup obj, CompositionContext context, int start)
            {
                bool versionBumped = false;
                Reconcile(obj, context, ref versionBumped);
                if (!versionBumped && (Start != start || _ownerVersion != owner.Version))
                    Version++;

                Start = start;
                _ownerVersion = owner.Version;
            }

            protected override void ApplyTo(FilterEffectContext context)
            {
                owner.ApplyChildren(context, Start);
            }
        }
    }
}
