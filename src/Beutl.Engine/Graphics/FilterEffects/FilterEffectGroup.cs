using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
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
        /// The children from the first continuation-controlling one onwards, when earlier effects precede it, either
        /// as earlier children or inside that child when it is a group.
        /// </summary>
        /// <remarks>
        /// The earlier children keep their own render nodes, such as a node graph effect that cannot be recorded
        /// by <see cref="FilterEffectContext"/>; only the continuation and the effects it controls share one node.
        /// </remarks>
        internal FilterEffect.Resource? ContinuationSuffixResource => _continuationSuffix;

        internal int ContinuationStart => _continuationSuffix?.Start ?? 0;

        /// <summary>The continuation-controlling child when it is a group that splits too.</summary>
        /// <remarks>Its earlier children also precede the continuation, so they keep their own nodes as well.</remarks>
        internal Resource? NestedContinuationGroup => _continuationSuffix?.Nested;

        protected override void ApplyTo(FilterEffectContext context)
        {
            context.ApplyTransactional(() =>
            {
                foreach (FilterEffect.Resource item in Children)
                {
                    context.Apply(item);
                }
            });
        }

        internal override PushedState PushInto(GraphicsContext2D context)
        {
            // A disabled group stays one node, which passes its input through; its children never get nodes of
            // their own, which would apply them.
            if (!IsEnabled) return Push(context);

            // The returned scope closes every node the group pushes, and none outside it when it has no children.
            int level = context.Depth + 1;
            if (!ControlsContinuation)
            {
                for (int i = Children.Count - 1; i >= 0; i--)
                {
                    context.PushFilterEffect(Children[i]);
                }

                return new PushedState(context, level);
            }

            if (ContinuationSuffixResource is { } suffix)
            {
                // The continuation and the effects it controls record into one node outside the earlier children,
                // which keep their own nodes and so need not be recordable by FilterEffectContext.
                suffix.Push(context);
                PushContinuationPrefix(context);
                return new PushedState(context, level);
            }

            return Push(context);
        }

        // Pushes outermost first, so a nested group's earlier children apply after the outer group's earlier children.
        private void PushContinuationPrefix(GraphicsContext2D context)
        {
            NestedContinuationGroup?.PushContinuationPrefix(context);
            for (int i = ContinuationStart - 1; i >= 0; i--)
            {
                context.PushFilterEffect(Children[i]);
            }
        }

        // A nested group that splits contributes only its suffix, since its earlier children have their own nodes.
        private void ApplyFromContinuation(FilterEffectContext context)
        {
            int start = ContinuationStart;
            Resource? nested = NestedContinuationGroup;
            context.ApplyTransactional(() =>
            {
                for (int i = start; i < Children.Count; i++)
                {
                    if (i == start && nested != null)
                        nested.ApplyFromContinuation(context);
                    else
                        context.Apply(Children[i]);
                }
            });
        }

        partial void PostReconcile(FilterEffectGroup obj, CompositionContext context)
        {
            int start = Children.FindIndex(static child => child.IsEnabled && child.ControlsContinuation);
            // Children reconcile first, so a nested group has already decided whether it splits.
            Resource? nested = start >= 0 && Children[start] is Resource { _continuationSuffix: not null } group
                ? group
                : null;
            if (start < 0 || (start == 0 && nested == null))
            {
                _continuationSuffix?.Dispose();
                _continuationSuffix = null;
                return;
            }

            _continuationSuffix ??= new ContinuationSuffix(this);
            _continuationSuffix.Reconcile(obj, context, start, nested);
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

            public Resource? Nested { get; private set; }

            public override bool ControlsContinuation => true;

            public override bool MayAccessTarget
            {
                get
                {
                    for (int i = Start; i < owner.Children.Count; i++)
                    {
                        FilterEffect.Resource applied = i == Start && Nested?._continuationSuffix is { } nested
                            ? nested
                            : owner.Children[i];
                        if (applied.MayAccessTarget) return true;
                    }

                    return false;
                }
            }

            public void Reconcile(FilterEffectGroup obj, CompositionContext context, int start, Resource? nested)
            {
                bool versionBumped = false;
                Reconcile(obj, context, ref versionBumped);
                if (!versionBumped && (Start != start || Nested != nested || _ownerVersion != owner.Version))
                    Version++;

                Start = start;
                Nested = nested;
                _ownerVersion = owner.Version;
            }

            protected override void ApplyTo(FilterEffectContext context)
            {
                owner.ApplyFromContinuation(context);
            }
        }
    }
}
