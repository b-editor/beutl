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

    public string? Name { get; }

    public IReadOnlyList<ChangeOperation> Operations => _operations;

    public bool HasOperations => _operations.Count > 0;

    public int OperationCount => _operations.Count;

    internal void AddOperation(ChangeOperation operation)
    {
        _operations.Add(operation);
    }

    internal void Apply(OperationExecutionContext context)
    {
        foreach (var operation in _operations)
        {
            operation.Apply(context);
        }
    }

    internal HistoryTransaction CreateRevertTransaction(
        OperationExecutionContext context,
        OperationSequenceGenerator sequenceGenerator,
        long transactionId)
    {
        var revertTransaction = new HistoryTransaction(transactionId, Name);

        // Create revert operations in reverse order
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            var revertOperation = _operations[i].CreateRevertOperation(context, sequenceGenerator);
            revertTransaction.AddOperation(revertOperation);
        }

        return revertTransaction;
    }
}
