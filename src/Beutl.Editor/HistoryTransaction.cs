using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class HistoryTransaction
{
    private readonly List<ChangeOperation> _operations = new();

    internal HistoryTransaction(long id, string? name = null)
    {
        Id = id;
        Name = name;
    }

    public long Id { get; }

    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public IReadOnlyList<ChangeOperation> Operations => _operations;

    public bool HasOperations => _operations.Count > 0;

    public int OperationCount => _operations.Count;

    internal void AddOperation(ChangeOperation operation)
    {
        _operations.Add(operation);
        CompactOperations();
    }

    private void CompactOperations()
    {
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                if (_operations[j] is IMergableChangeOperation mergableChangeOperation
                    && mergableChangeOperation.TryMerge(_operations[i]))
                {
                    _operations.RemoveAt(i);
                    break;
                }
            }
        }
    }

    internal void Apply(OperationExecutionContext context)
    {
        foreach (var operation in _operations)
        {
            operation.Apply(context);
        }
    }

    internal void Revert(OperationExecutionContext context)
    {
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            _operations[i].Revert(context);
        }
    }
}
