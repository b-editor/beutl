using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes.Group;

public class GroupOutput : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Left;

    public class GroupOutputSocket<T> : InputSocket<T>, IAutomaticallyGeneratedSocket, IGroupSocket
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
        if (socket is IOutputSocket { AssociatedType: { } valueType } outputSocket)
        {
            Type type = typeof(GroupOutputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is IInputSocket inputSocket)
            {
                CoreProperty? coreProperty = outputSocket.Property?.GetCoreProperty();
                ((NodeItem)inputSocket).LocalId = NextLocalId++;
                ((IGroupSocket)inputSocket).AssociatedProperty = coreProperty;
                if (coreProperty == null)
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

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (itemJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is IInputSocket socket)
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
