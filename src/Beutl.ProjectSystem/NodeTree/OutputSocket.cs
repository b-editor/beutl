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
    public static readonly CoreProperty<CoreList<Connection>> ConnectionsProperty;
    private readonly CoreList<Connection> _connections = [];
    private List<Guid>? _inputIds = null;
    // 型が一致していない、ソケットの数
    private int _unmatchSockets;
    private UnsafeBox<T> _box;

    static OutputSocket()
    {
        ConnectionsProperty = ConfigureProperty<CoreList<Connection>, OutputSocket<T>>(nameof(Connections))
            .Accessor(o => o.Connections, (o, v) => o.Connections = v)
            .Register();
    }

    public OutputSocket()
    {
        Connections.Attached += OnConnectionsAttached;
        Connections.Detached += OnConnectionsDetached;
    }

    ~OutputSocket()
    {
        _box.Dispose();
    }

    [NotAutoSerialized]
    public CoreList<Connection> Connections
    {
        get => _connections;
        set => _connections.Replace(value);
    }

    private void OnConnectionsDetached(Connection obj)
    {
        using var _ = PublishingSuppression.Enter();
        RaiseDisconnected(obj);
        obj.Input.NotifyDisconnected(obj);

        if (obj.Input is not InputSocket<T>)
        {
            _unmatchSockets--;
        }
    }

    private void OnConnectionsAttached(Connection obj)
    {
        using var _ = PublishingSuppression.Enter();
        RaiseConnected(obj);
        obj.Input.NotifyConnected(obj);

        if (obj.Input is not InputSocket<T>)
        {
            _unmatchSockets++;
        }
    }

    public void Disconnect(IInputSocket socket)
    {
        if (_connections.FirstOrDefault(x => x.Input == socket) is { } connection)
        {
            _connections.Remove(connection);
        }
    }

    public bool TryConnect(IInputSocket socket)
    {
        if (TryConnectCore(socket))
        {
            if (_inputIds?.Contains(socket.Id) == true)
            {
                _inputIds.Remove(socket.Id);

                if (_inputIds.Count == 0)
                {
                    _inputIds = null;
                }
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TryConnectCore(IInputSocket socket)
    {
        if (_connections.Any(x => x.Input == socket)
            || socket.Connection != null)
            return false;

        var connection = new Connection(socket, this);
        _connections.Add(connection);

        return true;
    }

    public override void PostEvaluate(EvaluationContext context)
    {
        base.PostEvaluate(context);
        if (_unmatchSockets > 0)
        {
            _box.Update(Value);
        }

        foreach (Connection item in _connections.GetMarshal().Value)
        {
            if (item.Input is InputSocket<T> sameTypeSocket)
            {
                sameTypeSocket.Receive(Value);
            }
            else
            {
                item.Input.Receive(_box.Object);
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("connection-inputs", Connections.Select(v => v.Input.Id).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<List<Guid>>("connection-inputs") is { } srcArray)
        {
            _inputIds = srcArray;
            TryRestoreConnection();
        }
    }

    private void TryRestoreConnection()
    {
        if (_inputIds != null)
        {
            for (int i = _inputIds.Count - 1; i >= 0; i--)
            {
                ISocket? socket = NodeTree?.FindSocket(_inputIds[i]);
                if (socket is IInputSocket inputSocket)
                {
                    if (TryConnectCore(inputSocket))
                    {
                        _inputIds.RemoveAt(i);
                    }
                }
            }

            if (_inputIds.Count == 0)
            {
                _inputIds = null;
            }
        }
    }

    protected override void OnAttachedToNodeTree(NodeTreeModel nodeTree)
    {
        base.OnAttachedToNodeTree(nodeTree);
        TryRestoreConnection();
    }

    protected override void OnDetachedFromNodeTree(NodeTreeModel nodeTree)
    {
        base.OnDetachedFromNodeTree(nodeTree);
        if (_inputIds != null)
        {
            _inputIds.Clear();
            _inputIds.EnsureCapacity(_connections.Count);
        }
        else
        {
            _inputIds = new(_connections.Count);
        }

        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            Connection item = _connections[i];

            _inputIds.Add(item.Input.Id);

            _connections.RemoveAt(i);
        }
    }
}
