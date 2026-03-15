using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.NodeGraph.Composition;
using Beutl.Serialization;

namespace Beutl.NodeGraph.Nodes;

public partial class LayerInputNode : GraphNode, IDynamicPortNode
{
    public NodePortLocation PossibleLocation => NodePortLocation.Right;

    public interface ILayerInputPort : IOutputPort, IDynamicPort
    {
        void SetupProperty(string propertyName);
    }

    public class LayerInputPort<T> : OutputPort<T>, ILayerInputPort
    {
        private NodePropertyAdapter<T>? _property;

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
                _property?.DisplayName = name;
            }

            if (display?.GetDescription() is { } description)
            {
                _property?.Description = description;
            }
        }

        public void SetupProperty(string propertyName)
        {
            _property = new NodePropertyAdapter<T>(propertyName);
            Property = _property;

            ReflectDisplay(Display);

            _property.Edited += OnPropertyEdited;
        }

        private void OnPropertyEdited(object? sender, EventArgs e)
        {
            RaiseEdited();
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            _property?.Serialize(context);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            string? name = context.GetValue<string>("Property");

            if (name != null)
            {
                SetupProperty(name);

                _property?.Deserialize(context);
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
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(ILayerInputPort)) is ILayerInputPort
                    port)
                {
                    Items.Add(port);
                }
            }
        }
    }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();

            // UseGlobalClock=false のアニメーションに対して、
            // Element.Start 分のオフセットを適用して再評価
            for (int i = 0; i < node.Items.Count; i++)
            {
                INodeMember item = node.Items[i];
                if (item.Property is IAnimatablePropertyAdapter animAdapter
                    && animAdapter.Animation is { UseGlobalClock: false } animation)
                {
                    var time = context.Time - node.Start;
                    ItemValues[i].TryLoadFromAnimation(animation, time);
                }
            }
        }
    }
}
