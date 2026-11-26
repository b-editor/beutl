using Beutl.Serialization;

namespace Beutl.NodeGraph;

[Flags]
public enum ConnectionStatus
{
    Disconnected = 0b1,
    Connected = 0b10,

    Success = Connected | 0b100,
    Convert = Connected | 0b1000,
    Error = Connected | 0b10000,
}

public sealed partial class Connection : Hierarchical
{
    public Connection(IInputPort input, IOutputPort output)
        : this()
    {
        Input.Value = new Reference<NodeMember>((NodeMember)input);
        Output.Value = new Reference<NodeMember>((NodeMember)output);
    }

    public Connection()
    {
        Input = new EndpointProperty(this, input: true);
        Output = new EndpointProperty(this, input: false);
        // Disconnected is a value rather than the default, as its CoreProperty form had no default.
        Status.Value = ConnectionStatus.Disconnected;
        ScanProperties<Connection>();
    }

    public IProperty<Reference<NodeMember>> Input { get; }

    public IProperty<Reference<NodeMember>> Output { get; }

    // Evaluation sets it again on every pass, so history leaves it out.
    [NotTracked]
    public IProperty<ConnectionStatus> Status { get; } = Property.CreateValue<ConnectionStatus>(default);

    // Diagnostic detail posted with the status by the graph evaluation.
    internal bool HasFailedPicture { get; set; }

    public void Connect()
    {
        if (Output.Value.Value is not IOutputPort outputNodePort || Input.Value.Value is not IInputPort inputNodePort)
            throw new InvalidOperationException();
        outputNodePort.NotifyConnected(this);
        inputNodePort.NotifyConnected(this);
    }

    public void Disconnect()
    {
        HasFailedPicture = false;
        if (Output.Value.Value is IOutputPort outputNodePort) outputNodePort.NotifyDisconnected(this);
        if (Input.Value.Value is IInputPort inputNodePort) inputNodePort.NotifyDisconnected(this);
        Status.Value = ConnectionStatus.Disconnected;
    }

    private sealed class EndpointProperty(Connection owner, bool input)
        : ValueOnlyProperty<Reference<NodeMember>>(default), IProperty
    {
        void IProperty.DeserializeValue(ICoreSerializationContext context)
        {
            if (context is JsonSerializationContext json
                && json.TryGetPreservedValue(Name, ValueType, out _, allowCollections: true)) return;
            if (context.GetValue<Optional<Reference<NodeMember>>>(Name) is not { HasValue: true, Value: var reference }) return;
            Value = reference;
            if (reference.IsNull) return;

            context.Resolve(reference.Id, resolved =>
            {
                // Both lists have attached by the time the context resolves references. A saved fallback
                // endpoint can share an Id with a real port belonging to a different connection.
                if (owner.HierarchicalParent is not GraphModel graph || graph.FindSavedFallbackPort(owner, input) == null)
                    Value = reference.Resolved((NodeMember)resolved);
            });
        }
    }
}
