namespace Beutl.Editor.Operations;

public enum ChangeOperationFailureState
{
    Unknown,
    Unchanged,
    Completed,
}

public abstract class ChangeOperation
{
    public required long SequenceNumber { get; set; }

    /// <summary>Describes the last failed Apply/Revert. Unknown failures cannot be replayed safely.</summary>
    public virtual ChangeOperationFailureState FailureState => ChangeOperationFailureState.Unknown;

    public abstract void Apply(OperationExecutionContext context);

    public abstract void Revert(OperationExecutionContext context);
}
