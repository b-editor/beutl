using System.Collections;
using System.Collections.Specialized;

using Beutl.Collections;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class Renderables : CoreList<Renderable>, IAffectsRender
{
    public Renderables()
    {
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void AddHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated += OnItemInvalidated;
            }
        }

        void RemoveHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated -= OnItemInvalidated;
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

    private void OnItemInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    private void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

}
