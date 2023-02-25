using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupOutput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Left;

    public class GroupOutputSocket<T> : InputSocket<T>, IAutomaticallyGeneratedSocket, IGroupSocket
    {
        static GroupOutputSocket()
        {
            NameProperty.OverrideMetadata<GroupOutputSocket<T>>(new CorePropertyMetadata<string>("name"));
        }

        public CoreProperty? AssociatedProperty { get; set; }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
            JsonNode propertyJson = json["associated-property"]!;
            string name = (string)propertyJson["name"]!;
            string owner = (string)propertyJson["owner"]!;

            Type ownerType = TypeFormat.ToType(owner)!;

            AssociatedProperty = PropertyRegistry.GetRegistered(ownerType)
                .FirstOrDefault(x => x.GetMetadata<CorePropertyMetadata>(ownerType).SerializeName == name || x.Name == name);
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            if (AssociatedProperty is { OwnerType: Type ownerType } property)
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
        }
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        connection = null;
        if (socket is IOutputSocket { AssociatedType: { } valueType } outputSocket)
        {
            Type type = typeof(GroupOutputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IInputSocket inputSocket)
            {
                ((NodeItem)inputSocket).LocalId = NextLocalId++;
                ((IGroupSocket)inputSocket).AssociatedProperty = outputSocket.Property?.Property;
                if (outputSocket.Property?.Property == null)
                {
                    ((CoreObject)inputSocket).Name = NodeDisplayNameHelper.GetDisplayName(outputSocket);
                }

                Items.Add(inputSocket);
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
                        IInputSocket? socket = null;

                        if (type?.IsAssignableTo(typeof(IInputSocket)) ?? false)
                        {
                            socket = Activator.CreateInstance(type) as IInputSocket;
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
