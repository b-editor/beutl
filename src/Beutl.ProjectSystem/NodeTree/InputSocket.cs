using System.ComponentModel;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.NodeTree;

public delegate bool InputSocketReceiver<T>(object? obj, out T? received);

public class InputSocket<T> : Socket<T>, IInputSocket
{
    private Guid _outputId;
    private InputSocketReceiver<T>? _onReceive;
    private bool _force;
    private TypeConverter? _dstTypeConverter;
    private TypeConverter? _srcTypeConverter;
    private Type? _srcType;

    public InputSocket()
    {
        _dstTypeConverter = TypeDescriptor.GetConverter(typeof(T));
    }

    public Connection? Connection { get; private set; }

    public void NotifyConnected(Connection connection)
    {
        if (_outputId == connection.Output.Id)
        {
            _outputId = Guid.Empty;
        }

        Connection = connection;
        UpdateStatus(ConnectionStatus.Connected);
        RaiseConnected(connection);
    }

    public void NotifyDisconnected(Connection connection)
    {
        if (Connection == connection)
        {
            Connection = null;
            RaiseDisconnected(connection);
        }
    }

    private void UpdateStatus(ConnectionStatus status)
    {
        Connection!.SetValue(Connection.StatusProperty, status);
    }

    public virtual void Receive(T? value)
    {
        if (Connection != null)
        {
            if (_force && _onReceive != null)
            {
                if (_onReceive(value, out T? received))
                {
                    Value = received;
                    UpdateStatus(ConnectionStatus.Convert);
                }
                else
                {
                    UpdateStatus(ConnectionStatus.Error);
                }
            }
            else
            {
                Value = value;
                UpdateStatus(ConnectionStatus.Success);
            }
        }
    }

    private void ReceiveWithConverter(object value)
    {
        ConnectionStatus status = ConnectionStatus.Error;
        try
        {
            Type srcType = value.GetType();

            if (_dstTypeConverter?.CanConvertFrom(value.GetType()) == true)
            {
                Value = (T?)_dstTypeConverter.ConvertFrom(value);
                status = ConnectionStatus.Convert;
            }
            else
            {
                if (_srcType != srcType
                    || _srcTypeConverter == null)
                {
                    _srcTypeConverter = TypeDescriptor.GetConverter(srcType);
                    _srcType = srcType;
                }

                if (_srcTypeConverter.CanConvertTo(typeof(T)))
                {
                    Value = (T?)_srcTypeConverter.ConvertTo(value, typeof(T));
                    status = ConnectionStatus.Convert;
                }
            }
        }
        catch
        {
            status = ConnectionStatus.Error;
        }
        finally
        {
            UpdateStatus(status);
        }
    }

    private void ReceiveInvalidValue()
    {
        T? value1 = default;
        if (Property != null)
        {
            value1 = Property.GetValue();
            if (value1 == null
                && Property?.GetCoreProperty()?.GetMetadata<CorePropertyMetadata<T>>(Property.ImplementedType) is { } metadata
                && metadata.HasDefaultValue)
            {
                value1 = metadata.DefaultValue;
            }
        }

        Receive(value1);
        UpdateStatus(ConnectionStatus.Error);
    }

    public void Receive(object? value)
    {
        if (value is T t)
        {
            Receive(t);
        }
        else
        {
            //if (_onReceive != null)
            //{
            //    IsValid = _onReceive(value, out T? received);
            //    if (IsValid.Value)
            //    {
            //        Value = received;
            //    }
            //}
            //else
            {
                if (value != null)
                {
                    ReceiveWithConverter(value);
                }
                else
                {
                    ReceiveInvalidValue();
                }
            }
        }
    }

    // force: 型が一致している場合でも、このReceiverを使います。
    public void RegisterReceiver(InputSocketReceiver<T> onReceive, bool force = false)
    {
        _onReceive = onReceive;
        _force = force;
    }

    public void RegisterConverter(TypeConverter typeConverter)
    {
        _dstTypeConverter = typeConverter;
    }

    public override void PreEvaluate(EvaluationContext context)
    {
        if (Connection == null)
        {
            Value = default;
            base.PreEvaluate(context);
        }
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue("connection-output", out var destNode)
            && destNode is JsonValue destValue
            && destValue.TryGetValue(out Guid outputId))
        {
            _outputId = outputId;
            TryRestoreConnection();
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        if (Connection != null)
        {
            json["connection-output"] = Connection.Output.Id;
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        _outputId = context.GetValue<Guid>("connection-output");
        TryRestoreConnection();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (Connection != null)
        {
            context.SetValue("connection-output", Connection.Output.Id);
        }
    }

    private void TryRestoreConnection()
    {
        if (Connection == null && _outputId != Guid.Empty)
        {
            ISocket? socket = NodeTree?.FindSocket(_outputId);
            if (socket is IOutputSocket outputSocket)
            {
                if (outputSocket.TryConnect(this))
                {
                    _outputId = Guid.Empty;
                }
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
        if (Connection != null && _outputId == Guid.Empty)
        {
            _outputId = Connection.Output.Id;
            Connection.Output.Disconnect(this);
        }
    }
}
