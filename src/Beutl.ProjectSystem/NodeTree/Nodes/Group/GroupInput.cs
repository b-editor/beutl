using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupInput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public class GroupInputSocket<T> : OutputSocket<T>, IGroupSocket, IAutomaticallyGeneratedSocket
    {
        public string? AssociatedPropertyName { get; set; }

        public Type? AssociatedPropertyType { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            context.SetValue(nameof(AssociatedPropertyName), AssociatedPropertyName);
            if (AssociatedPropertyType is { } type)
            {
                context.SetValue(nameof(AssociatedPropertyType), TypeFormat.ToString(type));
            }
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            AssociatedPropertyName = context.GetValue<string?>(nameof(AssociatedPropertyName));
            if (context.GetValue<string?>(nameof(AssociatedPropertyType)) is { } typeString)
            {
                AssociatedPropertyType = TypeFormat.ToType(typeString);
            }
        }

        private record CorePropertyRecord(string Name, string Owner);
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        var nodeTreeModel = this.FindRequiredHierarchicalParent<NodeTreeModel>();
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(GroupInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IOutputSocket outputSocket)
            {
                ((IGroupSocket)outputSocket).AssociatedPropertyName = inputSocket.Name;
                ((IGroupSocket)outputSocket).AssociatedPropertyType = valueType;

                Items.Add(outputSocket);
                connection = nodeTreeModel.Connect(inputSocket, outputSocket);
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
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(IOutputSocket)) is IOutputSocket socket)
                {
                    Items.Add(socket);
                }
            }
        }
    }
}
