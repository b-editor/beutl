using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public class LayerInputNode : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public interface ILayerInputSocket : IOutputSocket, IAutomaticallyGeneratedSocket
    {
        void SetupProperty(string propertyName);

        IPropertyAdapter? GetProperty();
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
                    ReflectDisplay(inputSocket.Display);
                }
            };
        }

        IPropertyAdapter? ILayerInputSocket.GetProperty()
        {
            return _property;
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
            RaiseEdited(new RenderInvalidatedEventArgs(this));
        }

        public NodePropertyAdapter<T>? GetProperty()
        {
            return _property;
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

        public override void PreEvaluate(EvaluationContext context)
        {
            if (GetProperty() is { } property)
            {
                if (property is IAnimatablePropertyAdapter<T> { Animation: { } animation })
                {
                    var time = context.Renderer.Time;
                    if (_parent != null && !animation.UseGlobalClock)
                    {
                        Value = animation.Interpolate(time - _parent.Start);
                    }
                    else
                    {
                        Value = animation.Interpolate(time);
                    }
                }
                else
                {
                    Value = property.GetValue();
                }
            }
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            GetProperty()?.Serialize(context);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            string? name = context.GetValue<string>("Property");

            if (name != null)
            {
                SetupProperty(name);

                GetProperty()?.Deserialize(context);
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
                outputSocket.GetProperty()?.SetValue(inputSocket.Property?.GetValue());

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
}
