using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Group;
using Beutl.Serialization;
using Beutl.Styling;

namespace Beutl.NodeTree.Nodes;

public class LayerInputNode : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public interface ILayerInputSocket : IOutputSocket, IAutomaticallyGeneratedSocket
    {
        void SetProperty(IAbstractProperty property);

        void SetupProperty(CoreProperty property);

        IAbstractProperty? GetProperty();
    }

    public class LayerInputSocket<T> : OutputSocket<T>, ILayerInputSocket, IGroupSocket
    {
        private SetterPropertyImpl<T>? _property;

        static LayerInputSocket()
        {
        }

        public CoreProperty? AssociatedProperty { get; set; }

        public void SetProperty(SetterPropertyImpl<T> property)
        {
            _property = property;
            AssociatedProperty = property.Property;

            property.Setter.Invalidated += OnSetterInvalidated;
        }

        void ILayerInputSocket.SetProperty(IAbstractProperty property)
        {
            SetProperty((SetterPropertyImpl<T>)property);
        }

        IAbstractProperty? ILayerInputSocket.GetProperty()
        {
            return _property;
        }

        public void SetupProperty(CoreProperty property)
        {
            var setter = new Setter<T>((CoreProperty<T>)property);
            SetProperty(new SetterPropertyImpl<T>(setter, property.OwnerType));
        }

        private void OnSetterInvalidated(object? sender, EventArgs e)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this));
        }

        public SetterPropertyImpl<T>? GetProperty()
        {
            return _property;
        }

        public override void PreEvaluate(EvaluationContext context)
        {
            if (GetProperty() is { } property)
            {
                if (property is IAbstractAnimatableProperty<T> { Animation: IAnimation<T> animation })
                {
                    Value = animation.GetAnimatedValue(context.Clock);
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
            string name = context.GetValue<string>("Property")!;
            string owner = context.GetValue<string>("Target")!;
            Type ownerType = TypeFormat.ToType(owner)!;

            AssociatedProperty = PropertyRegistry.GetRegistered(ownerType)
                .FirstOrDefault(x => x.Name == name);

            if (AssociatedProperty != null)
            {
                SetupProperty(AssociatedProperty);

                GetProperty()?.Deserialize(context);
            }
        }
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket
            && inputSocket.Property?.GetCoreProperty() is { } property)
        {
            Type type = typeof(LayerInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputSocket outputSocket)
            {
                ((NodeItem)outputSocket).LocalId = NextLocalId++;
                outputSocket.SetupProperty(property);
                outputSocket.GetProperty()?.SetValue(inputSocket.Property?.GetValue());

                Items.Add(outputSocket);
                if (outputSocket.TryConnect(inputSocket))
                {
                    connection = inputSocket.Connection!;
                    return true;
                }
                else
                {
                    Items.Remove(outputSocket);
                }
            }
        }

        return false;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (itemJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is ILayerInputSocket socket)
                {
                    if(socket is ICoreSerializable serializable)
                    {
                        if (LocalSerializationErrorNotifier.Current is not { } notifier)
                        {
                            notifier = NullSerializationErrorNotifier.Instance;
                        }
                        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

                        var innerContext = new JsonSerializationContext(type, notifier, parent, itemJson);
                        using (ThreadLocalSerializationContext.Enter(innerContext))
                        {
                            serializable.Deserialize(innerContext);
                        }
                    }

                    Items.Add(socket);
                    ((NodeItem)socket).LocalId = index;
                }

                index++;
            }

            NextLocalId = index;
        }
    }
}
