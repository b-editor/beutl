using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class HistoryTransaction
{
    private readonly List<ChangeOperation> _operations = new();
    // Recording observes operations already applied to the model. Track successful
    // execution per operation so retrying a partial undo/redo does not repeat it.
    private readonly List<bool> _applied = new();

    internal HistoryTransaction(long id, string? name = null)
    {
        Id = id;
        Name = name;
    }

    public long Id { get; }

    internal bool HasUncertainFailure { get; private set; }

    private void ExecuteOperation(OperationExecutionContext context, int index, bool apply)
    {
        try
        {
            if (apply) _operations[index].Apply(context); else _operations[index].Revert(context);
            _applied[index] = apply;
        }
        catch
        {
            if (_operations[index].FailureState == ChangeOperationFailureState.Completed)
                _applied[index] = apply;
            else if (_operations[index].FailureState != ChangeOperationFailureState.Unchanged)
                HasUncertainFailure = true;
            throw;
        }
    }

    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public IReadOnlyList<ChangeOperation> Operations => _operations;

    public bool HasOperations => _operations.Count > 0;

    public int OperationCount => _operations.Count;

    internal void AddOperation(ChangeOperation operation)
    {
        _operations.Add(operation);
        _applied.Add(true);
        CompactOperations();
    }

    private void CompactOperations()
    {
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                if (_applied[i] == _applied[j]
                    && _operations[j] is IMergableChangeOperation mergableChangeOperation
                    && mergableChangeOperation.TryMerge(_operations[i]))
                {
                    _operations.RemoveAt(i);
                    _applied.RemoveAt(i);
                    break;
                }
            }
        }
    }

    internal void Apply(OperationExecutionContext context)
    {
        if (HasUncertainFailure) throw new InvalidOperationException("This transaction has uncertain partial execution and cannot be replayed.");
        for (int i = 0; i < _operations.Count; i++)
        {
            if (_applied[i]) continue;
            ExecuteOperation(context, i, apply: true);
        }
    }

    internal void Revert(OperationExecutionContext context)
    {
        if (HasUncertainFailure) throw new InvalidOperationException("This transaction has uncertain partial execution and cannot be replayed.");
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            if (!_applied[i]) continue;
            ExecuteOperation(context, i, apply: false);
        }
    }
}
