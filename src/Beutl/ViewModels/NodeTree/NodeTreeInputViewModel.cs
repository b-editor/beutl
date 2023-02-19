using Beutl.Commands;
using Beutl.NodeTree.Nodes;
using Beutl.NodeTree.Nodes.Group;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeInputViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NodeTreeInputViewModel(Layer layer)
    {
        Model = layer;

        UseNode = layer.GetObservable(Layer.UseNodeProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        UseNode.Skip(1)
            //.ObserveOnRendererThread()
            .Subscribe(v => new ChangePropertyCommand<bool>(Model, Layer.UseNodeProperty, v, !v)
                                .DoAndRecord(CommandRecorder.Default))
            .DisposeWith(_disposables);

        layer.Space.Nodes.ForEachItem(
            (originalIdx, item) =>
            {
                if (item is LayerInputNode layerInput)
                {
                    int idx = ConvertFromOriginalIndex(originalIdx);
                    Items.Insert(idx, new NodeInputViewModel(layerInput, originalIdx, Model.Space));
                }
            },
            (originalIdx, item) =>
            {
                if (item is LayerInputNode layerInput)
                {
                    int idx = ConvertFromOriginalIndex(originalIdx);
                    Items[idx].Dispose();
                    Items.RemoveAt(idx);
                }
            },
            () =>
            {
                foreach (NodeInputViewModel item in Items.GetMarshal().Value)
                {
                    item.Dispose();
                }

                Items.Clear();
            }/*,
            scheduler: UIDispatcherScheduler.Default*/)
            .DisposeWith(_disposables);
    }

    public Layer Model { get; }

    public ReactiveProperty<bool> UseNode { get; }

    public CoreList<NodeInputViewModel> Items { get; } = new();

    // NodesのIndexから、ItemsのIndexに変換。
    public int ConvertFromOriginalIndex(int originalIndex)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].OriginalIndex == originalIndex)
            {
                return i;
            }
        }

        if (Items.Count > 0)
        {
            int lastIdx = Items[^1].OriginalIndex;
            if (lastIdx < originalIndex)
            {
                return Items.Count;
            }
            else
            {
                for (int i = 1; i < Items.Count; i++)
                {
                    if (Items[i - 1].OriginalIndex < originalIndex
                        && originalIndex <= Items[i].OriginalIndex)
                    {
                        return i;
                    }
                }
            }
        }

        return 0;
    }

    // ItemsのIndexから、NodesのIndexに変換。
    public int ConvertToOriginalIndex(int index)
    {
        return Items[index].OriginalIndex;
    }

    public void Dispose()
    {
        foreach (NodeInputViewModel item in Items.GetMarshal().Value)
        {
            item.Dispose();
        }

        Items.Clear();
        _disposables.Dispose();
    }
}
