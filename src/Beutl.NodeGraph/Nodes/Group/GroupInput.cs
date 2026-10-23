using System.Diagnostics.CodeAnalysis;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

public partial class GroupInput : GraphNode, IDynamicPortNode
{
    public NodePortLocation PossibleLocation => NodePortLocation.Right;

    public class GroupInputPort<T> : OutputPort<T>, IGroupPort, IDynamicPort
    {
        public GroupInputPort()
        {
            Connections.Attached += conn =>
            {
                if (conn is { Value.Input.Value.Value: InputPort<T> inputNodePort })
                {
                    ReflectDisplay(inputNodePort);
                }
            };
        }

        private void ReflectDisplay(InputPort<T> inputNodePort)
        {
            Name.Value = inputNodePort.Name.Value;
            Display.Value = inputNodePort.Display.Value;
        }
    }

    public bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection)
    {
        var graphModel = this.FindRequiredHierarchicalParent<GraphModel>();
        connection = null;
        if (port is IInputPort { AssociatedType: { } valueType } inputNodePort)
        {
            Type type = typeof(GroupInputPort<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IOutputPort outputNodePort)
            {
                connection = graphModel.Connect(inputNodePort, outputNodePort);
                Items.Add(outputNodePort);
                return true;
            }
        }

        return false;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        RestoreDynamicPorts<IOutputPort>(context);
    }

    public partial class Resource
    {
        public IItemValue[]? OuterInputValues { get; set; }

        public override void Update(GraphCompositionContext context)
        {
            if (OuterInputValues == null) return;

            for (int i = 0; i < ItemValues.Length && i < OuterInputValues.Length; i++)
            {
                ItemValues[i].PropagateFrom(OuterInputValues[i]);
            }
        }
    }
}
