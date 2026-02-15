using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.NodeTree.Rendering;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public partial class LayerInputNode : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public interface ILayerInputSocket : IOutputSocket, IAutomaticallyGeneratedSocket
    {
        void SetupProperty(string propertyName);
    }

    public class LayerInputSocket<T> : OutputSocket<T>, ILayerInputSocket
    {
        private Element? _parent;
        private NodePropertyAdapter<T>? _property;

        public LayerInputSocket()
        {
            Connections.Attached += conn =>
            {
                if (conn is { Value.Input.Value: InputSocket<T> inputSocket })
                {
                    Name = inputSocket.Name;
                    ReflectDisplay(inputSocket.Display);
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

        protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
        {
            base.OnAttachedToHierarchy(in args);
            _parent = this.FindHierarchicalParent<Element>();
        }

        protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
        {
            base.OnDetachedFromHierarchy(in args);
            _parent = null;
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

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        var nodeTreeModel = this.FindRequiredHierarchicalParent<NodeTreeModel>();
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(LayerInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputSocket outputSocket)
            {
                outputSocket.SetupProperty(inputSocket.Name);
                outputSocket.Property?.SetValue(inputSocket.Property?.GetValue());

                connection = nodeTreeModel.Connect(inputSocket, outputSocket);

                Items.Add(outputSocket);
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
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(ILayerInputSocket)) is ILayerInputSocket
                    socket)
                {
                    Items.Add(socket);
                }
            }
        }
    }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            var node = GetOriginal();

            // UseGlobalClock=false のアニメーションに対して、
            // Element.Start 分のオフセットを適用して再評価
            for (int i = 0; i < node.Items.Count; i++)
            {
                INodeItem item = node.Items[i];
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
