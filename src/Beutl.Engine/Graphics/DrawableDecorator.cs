using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

// Drawable継承しているが、Drawableのメソッドは使っていない
[Display(Name = nameof(GraphicsStrings.DrawableDecorator), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableDecorator : Drawable, IFlowOperator
{
    public DrawableDecorator()
    {
        ScanProperties<DrawableDecorator>();
        HideProperties(AlignmentX, AlignmentY);
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size;
            var boundsMemory = context.UseMemory<Rect>();
            var transformParams = (r.Transform, r.TransformOrigin, availableSize, boundsMemory);

            foreach (var child in r.Children)
            {
                using (context.PushBlendMode(r.BlendMode))
                using (context.PushNode(
                           transformParams,
                           b => new DrawableGroup.CustomTransformRenderNode(
                               b.Transform, b.TransformOrigin, b.availableSize,
                               Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory),
                           (n, b) => n.Update(
                               b.Transform, b.TransformOrigin, b.availableSize,
                               Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory)))
                using (context.PushOpacity(resource.Opacity / 100f))
                using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
                using (context.PushNode(
                           boundsMemory,
                           b => new DrawableGroup.BoundsObserveNode(b),
                           (n, b) => n.Update(b)))
                {
                    context.DrawDrawable(child);
                }
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }

    public new partial class Resource
    {
        private static readonly ReadOnlyCollection<Drawable.Resource> s_emptyChildren
            = Array.AsReadOnly(Array.Empty<Drawable.Resource>());

        private readonly List<Drawable.Resource> _children = [];
        private readonly PooledList<int> _childrenVersion = [];
        private IReadOnlyList<Drawable.Resource> _childrenSnapshot = s_emptyChildren;

        public IReadOnlyList<Drawable.Resource> Children => ReadGeneratedResourceState(ref _childrenSnapshot);

        partial void PostUpdate(DrawableDecorator obj, CompositionContext context)
        {
            EngineObject.Resource[]? flowRollbackSnapshot = context.Flow?.ToArray();
            using var consumed = new PooledList<Drawable.Resource>();
            if (context.Flow != null)
            {
                for (int i = context.Flow.Count - 1; i >= 0; i--)
                {
                    if (context.Flow[i] is Drawable.Resource d)
                    {
                        consumed.Insert(0, d);
                        context.Flow.RemoveAt(i);
                    }
                }
            }

            bool changed = false;
            try
            {
                ResourceReconciler.ReconcileListFromFlow(
                    context: context,
                    property: obj.Children,
                    consumed: consumed,
                    field: _children,
                    versions: _childrenVersion,
                    flowRollbackSnapshot: flowRollbackSnapshot,
                    changed: ref changed);
            }
            finally
            {
                PublishChildrenSnapshotIfChanged();
                if (changed)
                    Version++;
            }
        }

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (!disposing)
                return;

            int ownedStart = Math.Min(_childrenVersion.Count, _children.Count);
            for (int i = ownedStart; i < _children.Count; i++)
            {
                context.Reserve(_children[i]);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            Exception? failure = null;
            _children.Clear();
            Volatile.Write(ref _childrenSnapshot, s_emptyChildren);
            try
            {
                _childrenVersion.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            ThrowIfCleanupFailed(failure);
        }

        private void PublishChildrenSnapshotIfChanged()
        {
            IReadOnlyList<Drawable.Resource> current = Volatile.Read(ref _childrenSnapshot);
            if (current.Count == _children.Count)
            {
                bool matches = true;
                for (int i = 0; i < _children.Count; i++)
                {
                    if (!ReferenceEquals(current[i], _children[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return;
            }

            IReadOnlyList<Drawable.Resource> next = _children.Count == 0
                ? s_emptyChildren
                : Array.AsReadOnly(_children.ToArray());
            Volatile.Write(ref _childrenSnapshot, next);
        }
    }
}
