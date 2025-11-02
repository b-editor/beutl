namespace Beutl.Protocol.Operations;

/// <summary>
/// Defines the base contract for synchronization operations that mutate the object graph.
/// </summary>
public abstract class SyncOperation
{
    public required long SequenceNumber { get; set; }

    /// <summary>
    /// Applies the operation using the provided execution context.
    /// </summary>
    /// <param name="context">Context that supplies access to the synchronized root object.</param>
    public abstract void Apply(OperationExecutionContext context);
}
