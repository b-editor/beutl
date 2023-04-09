using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupInput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public class GroupInputSocket<T> : OutputSocket<T>, IGroupSocket, IAutomaticallyGeneratedSocket
    {
        public CoreProperty? AssociatedProperty { get; set; }

        public override void ReadFromJson(JsonObject json)
        {
            base.ReadFromJson(json);
            JsonNode propertyJson = json[nameof(AssociatedProperty)]!;
            string name = (string)propertyJson["Name"]!;
            string owner = (string)propertyJson["Owner"]!;

            Type ownerType = TypeFormat.ToType(owner)!;

            AssociatedProperty = PropertyRegistry.GetRegistered(ownerType)
                .FirstOrDefault(x => x.Name == name);
        }

        public override void WriteToJson(JsonObject json)
        {
            base.WriteToJson(json);
            if (AssociatedProperty is { OwnerType: Type ownerType } property)
            {
                string name = property.Name;
                string owner = TypeFormat.ToString(ownerType);

                json[nameof(AssociatedProperty)] = new JsonObject
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
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(GroupInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IOutputSocket outputSocket)
            {
                ((NodeItem)outputSocket).LocalId = NextLocalId++;
                ((IGroupSocket)outputSocket).AssociatedProperty = inputSocket.Property?.Property;
                if (inputSocket.Property?.Property == null)
                {
                    ((CoreObject)outputSocket).Name = NodeDisplayNameHelper.GetDisplayName(inputSocket);
                }

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

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue("Items", out var itemsNode)
            && itemsNode is JsonArray itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (itemJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is IOutputSocket socket)
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
