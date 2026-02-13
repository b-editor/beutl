using System.ComponentModel;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public delegate bool InputSocketReceiver<T>(object? obj, out T? received);

public class InputSocket<T> : Socket<T>, IInputSocket
{
    public static readonly CoreProperty<Reference<Connection>> ConnectionProperty;
    private InputSocketReceiver<T>? _onReceive;
    private bool _force;
    private TypeConverter? _dstTypeConverter;
    private TypeConverter? _srcTypeConverter;
    private Type? _srcType;

    static InputSocket()
    {
        ConnectionProperty = ConfigureProperty<Reference<Connection>, InputSocket<T>>(nameof(Connection))
            .Accessor(o => o.Connection, (o, v) => o.Connection = v)
            .Register();
    }

    public InputSocket()
    {
        InputSocketHelper.RegisterDefaultReceiver(this);
        _dstTypeConverter = TypeDescriptor.GetConverter(typeof(T));
    }

    public Reference<Connection> Connection
    {
        get;
        set => SetAndRaise(ConnectionProperty, ref field, value);
    }

    IObservable<Reference<Connection>> IInputSocket.GetConnectionObservable()
    {
        return this.GetObservable(ConnectionProperty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if(args is CorePropertyChangedEventArgs coreArgs && coreArgs.Property.Id == ConnectionProperty.Id)
        {
            RaiseTopologyChanged();
            RaiseEdited(EventArgs.Empty);
        }
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (!Connection.IsNull) throw new InvalidOperationException("This input socket is already connected.");
        Connection = connection;
        connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Connected);
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connection.IsNull || Connection.Id != connection.Id)
            throw new InvalidOperationException("This input socket is not connected to the specified connection.");
        Connection = default;
        connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Disconnected);
    }

    public virtual void Receive(T? value)
    {
        if (Connection.Value != null)
        {
            if (_force && _onReceive != null)
            {
                if (_onReceive(value, out T? received))
                {
                    Value = received;
                    Connection.Value.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Convert);
                }
                else
                {
                    Connection.Value.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Error);
                }
            }
            else
            {
                Value = value;
                Connection.Value.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Success);
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
            Connection.Value?.SetValue(Beutl.NodeTree.Connection.StatusProperty, status);
        }
    }

    private void ReceiveInvalidValue()
    {
        T? value1 = default;
        if (Property != null)
        {
            value1 = Property.GetValue();
            if (value1 == null
                && Property?.GetCoreProperty()?.GetMetadata<CorePropertyMetadata<T>>(Property.ImplementedType) is
                    { } metadata
                && metadata.HasDefaultValue)
            {
                value1 = metadata.DefaultValue;
            }
        }

        Receive(value1);

        Connection.Value?.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Error);
    }

    public void Receive(object? value)
    {
        if (value is T t)
        {
            Receive(t);
        }
        else
        {
            if (_onReceive?.Invoke(value, out T? received) == true)
            {
                Value = received;
                Connection.Value?.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Convert);
            }
            else
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
        if (Connection.Value == null)
        {
            Value = default;
            base.PreEvaluate(context);
        }
    }
}
