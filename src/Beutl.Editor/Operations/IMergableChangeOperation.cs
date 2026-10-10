namespace Beutl.Editor.Operations;

public interface IMergableChangeOperation
{
    bool TryMerge(ChangeOperation other);

    /// <summary>
    /// Gets whether applying or reverting the operation would leave the model unchanged, for example
    /// after merged writes returned a property to its starting value. A transaction drops a merged
    /// operation in this state, so it does not become an undo entry that changes nothing.
    /// </summary>
    bool IsNoop => false;
}
