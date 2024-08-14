using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;

namespace Beutl.Styling;

[ExcludeFromCodeCoverage]
public sealed class Setters : CoreList<ISetter>
{
    public Setters()
    {
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += AffectsRenders_CollectionChanged;
    }

    private void AffectsRenders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void AddHandlers(IList list)
        {
            foreach (ISetter? item in list.OfType<ISetter>())
            {
                item.Invalidated += Item_Invalidated;
            }
        }

        void RemoveHandlers(IList list)
        {
            foreach (ISetter? item in list.OfType<ISetter>())
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

        RaiseInvalidated();
    }

    public event EventHandler? Invalidated;

    private void Item_Invalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated();
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
