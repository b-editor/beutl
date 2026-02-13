using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupInput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public class GroupInputSocket<T> : OutputSocket<T>, IGroupSocket, IAutomaticallyGeneratedSocket
    {
        public GroupInputSocket()
        {
            Connections.Attached += conn =>
            {
                if (conn is { Value.Input.Value: InputSocket<T> inputSocket })
                {
                    ReflectDisplay(inputSocket);
                }
            };
        }

        private void ReflectDisplay(InputSocket<T> inputSocket)
        {
            Name = inputSocket.Name;
            Display = inputSocket.Display;
        }
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
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(IOutputSocket)) is IOutputSocket socket)
                {
                    Items.Add(socket);
                }
            }
        }
    }
}
