using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupOutput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Left;

    public class GroupOutputSocket<T> : InputSocket<T>, IAutomaticallyGeneratedSocket, IGroupSocket
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
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        var nodeTreeModel = this.FindRequiredHierarchicalParent<NodeTreeModel>();
        connection = null;
        if (socket is IOutputSocket { AssociatedType: { } valueType } outputSocket)
        {
            Type type = typeof(GroupOutputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IInputSocket inputSocket)
            {
                ((IGroupSocket)inputSocket).AssociatedPropertyName = outputSocket.Name;
                ((IGroupSocket)inputSocket).AssociatedPropertyType = valueType;

                Items.Add(inputSocket);
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
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(IInputSocket)) is IInputSocket socket)
                {
                    Items.Add(socket);
                }
            }
        }
    }
}
