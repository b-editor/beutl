using System.Collections.Specialized;

using BeUtl.Collections;
using BeUtl.ProjectSystem;

namespace BeUtl.Streaming;

public sealed class StreamOperators : CoreList<StreamOperator>
{
    public StreamOperators(Layer parent)
    {
        Parent = parent;
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
    }

    public Layer Parent { get; }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Span<StreamOperator> span = GetMarshal().Value;
        var args = new LogicalTreeAttachmentEventArgs(Parent);
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (int i = 0; i < e.NewStartingIndex; i++)
                {
                    (span[i] as ILogicalElement).NotifyDetachedFromLogicalTree(args);
                }

                for (int i = e.NewStartingIndex + e.NewItems!.Count; i < Count; i++)
                {
                    (span[i] as ILogicalElement).NotifyDetachedFromLogicalTree(args);
                }

                foreach (StreamOperator item in span)
                {
                    (item as ILogicalElement).NotifyAttachedToLogicalTree(args);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    (e.OldItems![i] as ILogicalElement)?.NotifyDetachedFromLogicalTree(args);
                }

                foreach (StreamOperator item in span)
                {
                    (item as ILogicalElement).NotifyDetachedFromLogicalTree(args);
                }

                foreach (StreamOperator item in span)
                {
                    (item as ILogicalElement).NotifyAttachedToLogicalTree(args);
                }
                break;

            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    (e.OldItems![i] as ILogicalElement)?.NotifyDetachedFromLogicalTree(args);
                }

                for (int i = 0; i < e.NewStartingIndex; i++)
                {
                    (span[i] as ILogicalElement).NotifyDetachedFromLogicalTree(args);
                }

                for (int i = e.NewStartingIndex + e.NewItems!.Count; i < Count; i++)
                {
                    (span[i] as ILogicalElement).NotifyDetachedFromLogicalTree(args);
                }

                foreach (StreamOperator item in span)
                {
                    (item as ILogicalElement).NotifyAttachedToLogicalTree(args);
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                break;

            default:
                break;
        }
    }
}
