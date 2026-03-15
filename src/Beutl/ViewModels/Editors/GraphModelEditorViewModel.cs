using System.Text.Json.Nodes;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class GraphModelEditorViewModel : ValueEditorViewModel<GraphModel?>
{
    private readonly CompositeDisposable _graphModelDisposables = [];

    public GraphModelEditorViewModel(IPropertyAdapter<GraphModel?> property)
        : base(property)
    {
        Value.Subscribe(v =>
            {
                DisposeNodeMembers();
                _graphModelDisposables.Clear();

                v?.Nodes.ForEachItem(
                        (originalIdx, item) =>
                        {
                            if (item is LayerInputNode layerInput)
                            {
                                int idx = ConvertFromOriginalIndex(originalIdx);
                                NodeMembers.Insert(idx,
                                    new GraphModelNodeMemberViewModel(layerInput, originalIdx, v, this));

                                for (int i = idx; i < NodeMembers.Count; i++)
                                {
                                    NodeMembers[i].OriginalIndex = v.Nodes.IndexOf(NodeMembers[i].GraphNode);
                                }
                            }
                        },
                        (originalIdx, item) =>
                        {
                            if (item is LayerInputNode)
                            {
                                int idx = ConvertFromOriginalIndex(originalIdx);
                                NodeMembers[idx].Dispose();
                                NodeMembers.RemoveAt(idx);

                                for (int i = idx; i < NodeMembers.Count; i++)
                                {
                                    NodeMembers[i].OriginalIndex = v.Nodes.IndexOf(NodeMembers[i].GraphNode);
                                }
                            }
                        },
                        DisposeNodeMembers)
                    .DisposeWith(_graphModelDisposables);

                AcceptChild();
            })
            .DisposeWith(Disposables);
    }

    public CoreList<GraphModelNodeMemberViewModel> NodeMembers { get; } = [];

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        foreach (GraphModelNodeMemberViewModel nodeMember in NodeMembers)
        {
            foreach (IPropertyEditorContext? prop in nodeMember.Properties)
            {
                prop?.Accept(visitor);
            }
        }
    }

    public void OpenNodeGraphTab()
    {
        if (this.GetService<IEditorContext>() is not { } editorContext) return;
        if (Value.Value == null) return;

        NodeGraphTabViewModel tab = editorContext.FindToolTab<NodeGraphTabViewModel>()
                                   ?? new NodeGraphTabViewModel(editorContext);

        tab.Model.Value = Value.Value;

        editorContext.OpenToolTab(tab);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeNodeMembers();
        _graphModelDisposables.Dispose();
    }

    private void DisposeNodeMembers()
    {
        foreach (GraphModelNodeMemberViewModel item in NodeMembers.GetMarshal().Value)
        {
            item.Dispose();
        }

        NodeMembers.Clear();
    }

    private int ConvertFromOriginalIndex(int originalIndex)
    {
        for (int i = 0; i < NodeMembers.Count; i++)
        {
            if (NodeMembers[i].OriginalIndex == originalIndex)
            {
                return i;
            }
        }

        if (NodeMembers.Count > 0)
        {
            int lastIdx = NodeMembers[^1].OriginalIndex;
            if (lastIdx < originalIndex)
            {
                return NodeMembers.Count;
            }
            else
            {
                for (int i = 1; i < NodeMembers.Count; i++)
                {
                    if (NodeMembers[i - 1].OriginalIndex < originalIndex
                        && originalIndex <= NodeMembers[i].OriginalIndex)
                    {
                        return i;
                    }
                }
            }
        }

        return 0;
    }

    private sealed record Visitor(GraphModelEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(GraphModelEditorViewModel))
                return Obj;

            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
