using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class LayerInputNode : GraphNode, IDynamicPortNode
{
    public NodePortLocation PossibleLocation => NodePortLocation.Right;

    public interface ILayerInputPort : IOutputPort, IDynamicPort
    {
        void SetupProperty(string propertyName);
    }

    // Earlier builds also wrote the adapter type under Target, which nothing has read since dbf5054eb. It is dropped
    // rather than kept as an unknown member.
    [DroppedMembers("Target")]
    public class LayerInputPort<T> : OutputPort<T>, ILayerInputPort
    {
        public LayerInputPort()
        {
            Connections.Attached += conn =>
            {
                if (conn is { Value.Input.Value: InputPort<T> inputNodePort })
                {
                    Name = inputNodePort.Name;
                    ReflectDisplay(inputNodePort.Display);
                }
            };
        }

        private void ReflectDisplay(DisplayAttribute? display)
        {
            Display = display;

            if (display?.GetName() is { } name)
            {
                PropertyAdapter?.DisplayName = name;
            }

            if (display?.GetDescription() is { } description)
            {
                PropertyAdapter?.Description = description;
            }
        }

        public void SetupProperty(string propertyName)
        {
            SetPropertyAdapterCore(new NodePropertyAdapter<T>(propertyName));

            ReflectDisplay(Display);
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            PropertyAdapter?.Serialize(context);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            string? name = context.GetValue<string>("Property");

            if (name != null)
            {
                SetupProperty(name);

                PropertyAdapter?.Deserialize(context);
            }
        }
    }

    public bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection)
    {
        var graphModel = this.FindRequiredHierarchicalParent<GraphModel>();
        connection = null;
        if (port is IInputPort { AssociatedType: { } valueType } inputNodePort)
        {
            Type type = typeof(LayerInputPort<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputPort outputNodePort)
            {
                outputNodePort.SetupProperty(inputNodePort.Name);
                outputNodePort.Property?.SetValue(inputNodePort.Property?.GetValue());

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
        RestoreDynamicPorts<ILayerInputPort>(context);
    }
}
