using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.NodeTree.Rendering;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public partial class GroupOutput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Left;

    public class GroupOutputSocket<T> : InputSocket<T>, IAutomaticallyGeneratedSocket, IGroupSocket
    {
        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            if (args is not CorePropertyChangedEventArgs coreArgs) return;

            if (coreArgs.Property.Id == ConnectionProperty.Id)
            {
                if (Connection.Value?.Output.Value is OutputSocket<T> outputSocket)
                {
                    ReflectDisplay(outputSocket);
                }
            }
        }

        private void ReflectDisplay(OutputSocket<T> outputSocket)
        {
            Name = outputSocket.Name;
            Display = outputSocket.Display;
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
                connection = nodeTreeModel.Connect(inputSocket, outputSocket);
                Items.Add(inputSocket);
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
