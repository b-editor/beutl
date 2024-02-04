using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupInput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public class GroupInputSocket<T> : OutputSocket<T>, IGroupSocket, IAutomaticallyGeneratedSocket
    {
        public CoreProperty? AssociatedProperty { get; set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (AssociatedProperty is { OwnerType: Type ownerType } property)
            {
                context.SetValue(
                    nameof(AssociatedProperty),
                    new CorePropertyRecord(property.Name, TypeFormat.ToString(ownerType)));
            }
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            if (context.GetValue<CorePropertyRecord>(nameof(AssociatedProperty)) is { } prop)
            {
                Type ownerType = TypeFormat.ToType(prop.Owner)!;

                AssociatedProperty = PropertyRegistry.GetRegistered(ownerType)
                    .FirstOrDefault(x => x.Name == prop.Name);
            }
        }

        private record CorePropertyRecord(string Name, string Owner);
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(GroupInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IOutputSocket outputSocket)
            {
                CoreProperty? coreProperty = inputSocket.Property?.GetCoreProperty();
                ((NodeItem)outputSocket).LocalId = NextLocalId++;
                ((IGroupSocket)outputSocket).AssociatedProperty = coreProperty;
                if (coreProperty == null)
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

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (itemJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is IOutputSocket socket)
                {
                    if (socket is ICoreSerializable serializable)
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
