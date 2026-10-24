using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

// The connection list OutputPort and ListInputPort keep: membership by Id and its
// "Connections" serialized form.
internal static class ConnectionReferenceList
{
    public static bool AddIfMissing(ICoreList<Reference<Connection>> connections, Connection connection)
    {
        if (connections.All(r => r.Id != connection.Id.Value))
        {
            connections.Add(connection);
            return true;
        }

        return false;
    }

    public static bool RemoveIfPresent(ICoreList<Reference<Connection>> connections, Connection connection)
    {
        if (connections.Any(r => r.Id == connection.Id.Value))
        {
            connections.Remove(connection);
            return true;
        }

        return false;
    }

    public static void Write(ICoreSerializationContext context, ICoreList<Reference<Connection>> connections)
    {
        context.SetValue("Connections", connections.ToArray());
    }

    public static void Read(ICoreSerializationContext context, ICoreList<Reference<Connection>> connections)
    {
        if (context.GetValue<List<Guid>>("Connections") is { } srcArray)
        {
            connections.Replace(srcArray.Select(id => new Reference<Connection>(id)).ToArray());
            for (int i = 0; i < connections.Count; i++)
            {
                int index = i;
                Reference<Connection> reference = connections[i];
                context.Resolve(reference.Id, o => connections[index] = (Connection)o);
            }
        }
    }
}
