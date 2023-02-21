using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Media;
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

    public class LayerInputSocket<T> : OutputSocket<T>, ILayerInputSocket
    {
        private SetterPropertyImpl<T>? _property;
        private CoreProperty? _associatedProperty;
        private IDisposable? _disposable;
        private bool _hasAnimation = false;

        static LayerInputSocket()
        {
            NameProperty.OverrideMetadata<LayerInputSocket<T>>(new CorePropertyMetadata<string>("name"));
        }

        public void SetProperty(SetterPropertyImpl<T> property)
        {
            _disposable?.Dispose();

            _property = property;
            _associatedProperty = property.Property;

            property.Setter.Invalidated += OnSetterInvalidated;
            _disposable = property.HasAnimation.Subscribe(v => _hasAnimation = v);
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
                if (_hasAnimation && property is IAbstractAnimatableProperty<T> animatableProperty)
                {
                    Value = animatableProperty.Animation.Interpolate(context.Clock.CurrentTime);
                }
                else
                {
                    Value = property.GetValue();
                }
            }
        }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
            JsonNode propertyJson = json["associated-property"]!;
            string name = (string)propertyJson["name"]!;
            string owner = (string)propertyJson["owner"]!;

            Type ownerType = TypeFormat.ToType(owner)!;

            _associatedProperty = PropertyRegistry.GetRegistered(ownerType)
                .FirstOrDefault(x => x.GetMetadata<CorePropertyMetadata>(ownerType).SerializeName == name || x.Name == name);

            if (_associatedProperty != null)
            {
                SetupProperty(_associatedProperty);

                GetProperty()?.ReadFromJson(json);
            }
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            if (_associatedProperty is { OwnerType: Type ownerType } property)
            {
                CorePropertyMetadata? metadata = property.GetMetadata<CorePropertyMetadata>(ownerType);
                string name = metadata.SerializeName ?? property.Name;
                string owner = TypeFormat.ToString(ownerType);

                json["associated-property"] = new JsonObject
                {
                    ["name"] = name,
                    ["owner"] = owner,
                };
            }

            GetProperty()?.WriteToJson(ref json);
        }
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out IConnection? connection)
    {
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType, Property.Property: { } property } inputSocket)
        {
            Type type = typeof(LayerInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputSocket outputSocket)
            {
                ((NodeItem)outputSocket).LocalId = NextLocalId++;
                ((CoreObject)outputSocket).Name = NodeDisplayNameHelper.GetDisplayName(inputSocket);
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

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("items", out var itemsNode)
                && itemsNode is JsonArray itemsArray)
            {
                int index = 0;
                foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
                {
                    if (itemJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeFormat.ToType(atType);
                        ILayerInputSocket? socket = null;

                        if (type?.IsAssignableTo(typeof(ILayerInputSocket)) ?? false)
                        {
                            socket = Activator.CreateInstance(type) as ILayerInputSocket;
                        }

                        if (socket != null)
                        {
                            (socket as IJsonSerializable)?.ReadFromJson(itemJson);
                            Items.Add(socket);
                            ((NodeItem)socket).LocalId = index;
                        }
                    }

                    index++;
                }

                NextLocalId = index;
            }
        }
    }
}
