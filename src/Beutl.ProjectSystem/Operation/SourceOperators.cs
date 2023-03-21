using System.Collections.Specialized;

using Beutl.Collections;
using Beutl.ProjectSystem;

namespace Beutl.Operation;

public sealed class SourceOperators : CoreList<SourceOperator>
{
    public SourceOperators(Layer parent)
    {
        Parent = parent;
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
    }

    public Layer Parent { get; }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Span<SourceOperator> span = GetMarshal().Value;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (int i = 0; i < e.NewStartingIndex; i++)
                {
                    (span[i] as IModifiableHierarchical).SetParent(null);
                }

                for (int i = e.NewStartingIndex + e.NewItems!.Count; i < Count; i++)
                {
                    (span[i] as IModifiableHierarchical).SetParent(null);
                }

                foreach (SourceOperator item in span)
                {
                    (item as IModifiableHierarchical).SetParent(Parent);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    (e.OldItems![i] as IModifiableHierarchical)!.SetParent(null);
                }

                foreach (SourceOperator item in span)
                {
                    (item as IModifiableHierarchical).SetParent(null);
                }

                foreach (SourceOperator item in span)
                {
                    (item as IModifiableHierarchical).SetParent(Parent);
                }
                break;

            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    (e.OldItems![i] as IModifiableHierarchical)!.SetParent(null);
                }

                for (int i = 0; i < e.NewStartingIndex; i++)
                {
                    (span[i] as IModifiableHierarchical).SetParent(null);
                }

                for (int i = e.NewStartingIndex + e.NewItems!.Count; i < Count; i++)
                {
                    (span[i] as IModifiableHierarchical).SetParent(null);
                }

                foreach (SourceOperator item in span)
                {
                    (item as IModifiableHierarchical).SetParent(Parent);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                break;

            default:
                break;
        }
    }
}
