using System.Diagnostics.CodeAnalysis;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

public partial class GroupOutput : GraphNode, IDynamicPortNode
{
    public NodePortLocation PossibleLocation => NodePortLocation.Left;

    public class GroupOutputPort<T> : InputPort<T>, IDynamicPort, IGroupPort
    {
        private protected override void OnConnectionChanged()
        {
            base.OnConnectionChanged();
            if (Connection.Value.Value?.Output.Value.Value is OutputPort<T> outputNodePort)
            {
                ReflectDisplay(outputNodePort);
            }
        }

        private void ReflectDisplay(OutputPort<T> outputNodePort)
        {
            Name.Value = outputNodePort.Name.Value;
            Display.Value = outputNodePort.Display.Value;
        }
    }

    public bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection)
    {
        var graph = this.FindRequiredHierarchicalParent<GraphModel>();
        connection = null;
        if (port is IOutputPort { AssociatedType: { } valueType } outputNodePort)
        {
            Type type = typeof(GroupOutputPort<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IInputPort inputNodePort)
            {
                connection = graph.Connect(inputNodePort, outputNodePort);
                Items.Add(inputNodePort);
                return true;
            }
        }

        return false;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        RestoreDynamicPorts<IInputPort>(context);
    }
}
