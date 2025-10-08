using System.Collections;
using System.Collections.Specialized;
using Beutl.Collections;
using Beutl.Media;

namespace Beutl.Graphics.Transformation;

// IAffectsRender, IHierarchical cannot be implemented in ITransform,
// so the contents of the AffectsRenders, HierarchicalList are implemented manually
public sealed class Transforms : CoreList<Transform>, IAffectsRender
{
    public Transforms()
    {
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
    }

    public Transforms(IModifiableHierarchical parent)
    {
        Parent = parent;
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
        Attached += item =>
        {
            if (item is not IHierarchical child) return;
            Parent.AddChild(child);
        };
        Detached += item =>
        {
            if (item is not IHierarchical child) return;
            Parent.RemoveChild(child);
        };
    }

    public IModifiableHierarchical? Parent { get; }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void AddHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated += Item_Invalidated;
            }
        }

        void RemoveHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated -= Item_Invalidated;
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                AddHandlers(e.NewItems);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                RemoveHandlers(e.OldItems);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
                AddHandlers(e.NewItems);
                RemoveHandlers(e.OldItems);
                break;
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
            default:
                break;
        }

        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    private void Item_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    private void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
