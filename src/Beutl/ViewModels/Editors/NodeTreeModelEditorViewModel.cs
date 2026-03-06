using System.Text.Json.Nodes;
using Beutl.Editor.Components.NodeTreeTab.ViewModels;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class NodeTreeModelEditorViewModel : ValueEditorViewModel<NodeTreeModel?>
{
    private readonly CompositeDisposable _nodeTreeDisposables = [];

    public NodeTreeModelEditorViewModel(IPropertyAdapter<NodeTreeModel?> property)
        : base(property)
    {
        Value.Subscribe(v =>
            {
                DisposeNodeItems();
                _nodeTreeDisposables.Clear();

                v?.Nodes.ForEachItem(
                        (originalIdx, item) =>
                        {
                            if (item is LayerInputNode layerInput)
                            {
                                int idx = ConvertFromOriginalIndex(originalIdx);
                                NodeItems.Insert(idx,
                                    new NodeTreeModelNodeItemViewModel(layerInput, originalIdx, v, this));

                                for (int i = idx; i < NodeItems.Count; i++)
                                {
                                    NodeItems[i].OriginalIndex = v.Nodes.IndexOf(NodeItems[i].Node);
                                }
                            }
                        },
                        (originalIdx, item) =>
                        {
                            if (item is LayerInputNode)
                            {
                                int idx = ConvertFromOriginalIndex(originalIdx);
                                NodeItems[idx].Dispose();
                                NodeItems.RemoveAt(idx);

                                for (int i = idx; i < NodeItems.Count; i++)
                                {
                                    NodeItems[i].OriginalIndex = v.Nodes.IndexOf(NodeItems[i].Node);
                                }
                            }
                        },
                        DisposeNodeItems)
                    .DisposeWith(_nodeTreeDisposables);

                AcceptChild();
            })
            .DisposeWith(Disposables);
    }

    public CoreList<NodeTreeModelNodeItemViewModel> NodeItems { get; } = [];

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        foreach (NodeTreeModelNodeItemViewModel nodeItem in NodeItems)
        {
            foreach (IPropertyEditorContext? prop in nodeItem.Properties)
            {
                prop?.Accept(visitor);
            }
        }
    }

    public void OpenNodeTreeTab()
    {
        if (this.GetService<IEditorContext>() is not { } editorContext) return;
        if (Value.Value == null) return;

        NodeTreeTabViewModel tab = editorContext.FindToolTab<NodeTreeTabViewModel>()
                                   ?? new NodeTreeTabViewModel(editorContext);

        tab.Model.Value = Value.Value;

        editorContext.OpenToolTab(tab);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeNodeItems();
        _nodeTreeDisposables.Dispose();
    }

    private void DisposeNodeItems()
    {
        foreach (NodeTreeModelNodeItemViewModel item in NodeItems.GetMarshal().Value)
        {
            item.Dispose();
        }

        NodeItems.Clear();
    }

    private int ConvertFromOriginalIndex(int originalIndex)
    {
        for (int i = 0; i < NodeItems.Count; i++)
        {
            if (NodeItems[i].OriginalIndex == originalIndex)
            {
                return i;
            }
        }

        if (NodeItems.Count > 0)
        {
            int lastIdx = NodeItems[^1].OriginalIndex;
            if (lastIdx < originalIndex)
            {
                return NodeItems.Count;
            }
            else
            {
                for (int i = 1; i < NodeItems.Count; i++)
                {
                    if (NodeItems[i - 1].OriginalIndex < originalIndex
                        && originalIndex <= NodeItems[i].OriginalIndex)
                    {
                        return i;
                    }
                }
            }
        }

        return 0;
    }

    private sealed record Visitor(NodeTreeModelEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(NodeTreeModelEditorViewModel))
                return Obj;

            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
