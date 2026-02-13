using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Serialization;

namespace Beutl.NodeTree;

internal struct UnsafeBox<T> : IDisposable
{
    public object? Object;
    private GCHandle _handle;
    private nint _ptr;

    public unsafe void Update(T? value)
    {
        bool managedType = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        if (!managedType)
        {
            if (Object != null && _handle.IsAllocated)
            {
                Unsafe.Write((void*)_ptr, value);
            }
            else
            {
                Object = value;
                _handle = GCHandle.Alloc(Object, GCHandleType.Pinned);
                _ptr = _handle.AddrOfPinnedObject();
            }
        }
        else
        {
            Object = value;
        }
    }

    public void Dispose()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }
}

public class OutputSocket<T> : Socket<T>, IOutputSocket
{
    public static readonly CoreProperty<CoreList<Reference<Connection>>> ConnectionsProperty;
    private readonly CoreList<Reference<Connection>> _connections = [];
    private UnsafeBox<T> _box;

    static OutputSocket()
    {
        ConnectionsProperty = ConfigureProperty<CoreList<Reference<Connection>>, OutputSocket<T>>(nameof(Connections))
            .Accessor(o => o.Connections, (o, v) => o.Connections = v)
            .Register();
    }

    public OutputSocket()
    {
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited(EventArgs.Empty);
        };
    }

    ~OutputSocket()
    {
        _box.Dispose();
    }

    [NotAutoSerialized]
    public CoreList<Reference<Connection>> Connections
    {
        get => _connections;
        set => _connections.Replace(value);
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (Connections.All(r => r.Id != connection.Id))
        {
            Connections.Add(connection);
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connections.Any(r => r.Id == connection.Id))
        {
            Connections.Remove(connection);
        }
    }

    public override void PostEvaluate(EvaluationContext context)
    {
        base.PostEvaluate(context);
        bool boxUpdated = false;
        foreach (Reference<Connection> item in _connections)
        {
            if (item.Value == null) continue;

            if (item.Value.Input.Value is InputSocket<T> sameTypeSocket)
            {
                sameTypeSocket.Receive(Value);
            }
            else if(item.Value.Input.Value is IInputSocket inputSocket)
            {
                if (!boxUpdated)
                {
                    _box.Update(Value);
                    boxUpdated = true;
                }

                inputSocket.Receive(_box.Object);
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Connections", Connections.Select(v => v.Id).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<List<Guid>>("Connections") is { } srcArray)
        {
            Connections.Replace(srcArray.Select(id => new Reference<Connection>(id)).ToArray());
            for (int i = 0; i < Connections.Count; i++)
            {
                int index = i;
                Reference<Connection> reference = Connections[i];
                context.Resolve(reference.Id, o =>
                {
                    Connections[index] = (Connection)o;
                });
            }
        }
    }
}
