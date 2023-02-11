using System.Text.Json.Nodes;

namespace Beutl.NodeTree;

public class InputSocket<T> : Socket<T>, IInputSocket<T>
{
    private Guid _outputId;

    public IConnection? Connection { get; private set; }

    public void NotifyConnected(IConnection connection)
    {
        if (_outputId == connection.Output.Id)
        {
            _outputId = Guid.Empty;
        }

        Connection = connection;
        RaiseConnected(connection);
    }

    public void NotifyDisconnected(IConnection connection)
    {
        if (Connection == connection)
        {
            Connection = null;
            RaiseDisconnected(connection);
        }
    }

    public virtual void Receive(T? value)
    {
        Value = value;
    }

    public override void PreEvaluate(EvaluationContext context)
    {
        if (Connection == null)
        {
            Value = default;
            base.PreEvaluate(context);
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("connection-output", out var destNode)
                && destNode is JsonValue destValue
                && destValue.TryGetValue(out Guid outputId))
            {
                _outputId = outputId;
                TryRestoreConnection();
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (Connection != null)
        {
            json["connection-output"] = Connection.Output.Id;
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

    protected override void OnAttachedToNodeTree(NodeTreeSpace nodeTree)
    {
        base.OnAttachedToNodeTree(nodeTree);
        TryRestoreConnection();
    }

    protected override void OnDetachedFromNodeTree(NodeTreeSpace nodeTree)
    {
        base.OnDetachedFromNodeTree(nodeTree);
        if (Connection != null && _outputId == Guid.Empty)
        {
            _outputId = Connection.Output.Id;
            Connection.Output.Disconnect(this);
        }
    }
}
