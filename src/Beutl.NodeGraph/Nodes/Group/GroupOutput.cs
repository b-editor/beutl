using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes.Group;

public partial class GroupOutput : GraphNode, IDynamicPortNode
{
    public NodePortLocation PossibleLocation => NodePortLocation.Left;

    public class GroupOutputPort<T> : InputPort<T>, IDynamicPort, IGroupPort
    {
        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            if (args is not CorePropertyChangedEventArgs coreArgs) return;

            if (coreArgs.Property.Id == ConnectionProperty.Id)
            {
                if (Connection.Value?.Output.Value is OutputPort<T> outputNodePort)
                {
                    ReflectDisplay(outputNodePort);
                }
            }
        }

        private void ReflectDisplay(OutputPort<T> outputNodePort)
        {
            Name = outputNodePort.Name;
            Display = outputNodePort.Display;
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
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(IInputPort)) is IInputPort port)
                {
                    Items.Add(port);
                }
            }
        }
    }
}
