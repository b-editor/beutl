using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupOutput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Left;

    public class GroupOutputSocket<T> : InputSocket<T>, IAutomaticallyGeneratedSocket, IGroupSocket
    {
        public CoreProperty? AssociatedProperty { get; set; }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
            JsonNode propertyJson = json[nameof(AssociatedProperty)]!;
            string name = (string)propertyJson["Name"]!;
            string owner = (string)propertyJson["Owner"]!;

            Type ownerType = TypeFormat.ToType(owner)!;

            AssociatedProperty = PropertyRegistry.GetRegistered(ownerType)
                .FirstOrDefault(x => x.Name == name);
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            if (AssociatedProperty is { OwnerType: Type ownerType } property)
            {
                string name = property.Name;
                string owner = TypeFormat.ToString(ownerType);

                json["AssociatedProperty"] = new JsonObject
                {
                    ["Name"] = name,
                    ["Owner"] = owner,
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
            if (obj.TryGetPropertyValue("Items", out var itemsNode)
                && itemsNode is JsonArray itemsArray)
            {
                int index = 0;
                foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
                {
                    if (itemJson.TryGetDiscriminator(out Type? type)
                        && Activator.CreateInstance(type) is IInputSocket socket)
                    {
                        (socket as IJsonSerializable)?.ReadFromJson(itemJson);
                        Items.Add(socket);
                        ((NodeItem)socket).LocalId = index;
                    }

                    index++;
                }

                NextLocalId = index;
            }
        }
    }
}
