using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class HistoryTransaction
{
    private readonly List<ChangeOperation> _operations = new();
    private readonly Dictionary<MergeKey, int> _mergeCandidates = [];
    private bool _hasCustomMerges;
    // Recording observes operations already applied to the model. Track successful
    // execution per operation so retrying a partial undo/redo does not repeat it.
    private readonly List<bool> _applied = new();

    internal HistoryTransaction(long id)
    {
        Id = id;
    }

    public long Id { get; }

    internal bool HasUncertainFailure { get; private set; }

    private void ExecuteOperation(int index, bool apply)
    {
        try
        {
            if (apply) _operations[index].Apply(); else _operations[index].Revert();
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
        if (!_hasCustomMerges && TryGetMergeKey(operation, out MergeKey key))
        {
            if (!_mergeCandidates.TryGetValue(key, out int index))
            {
                _mergeCandidates[key] = _operations.Count - 1;
                return;
            }
            if (_applied[index] && _operations[index] is IMergableChangeOperation candidate
                && candidate.TryMerge(operation))
            {
                _operations.RemoveAt(_operations.Count - 1);
                _applied.RemoveAt(_applied.Count - 1);
                if (candidate.IsNoop)
                {
                    _operations.RemoveAt(index);
                    _applied.RemoveAt(index);
                    RebuildMergeCandidates();
                }
                return;
            }
        }
        else if (operation is IMergableChangeOperation)
        {
            // Extension operations may merge across identities. Preserve their existing policy.
            _hasCustomMerges = true;
            _mergeCandidates.Clear();
        }
        else if (!_hasCustomMerges)
        {
            return;
        }

        CompactOperations();
        if (!_hasCustomMerges) RebuildMergeCandidates();
    }

    // The built-in setters merge only writes of the same type to the same object and property.
    // Indexing their merge candidates avoids scanning prior operations for every step of a bulk edit.
    private static bool TryGetMergeKey(ChangeOperation operation, out MergeKey key)
    {
        Type type = operation.GetType();
        switch (operation)
        {
            case IUpdatePropertyValueOperation value when type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(UpdatePropertyValueOperation<>):
                key = new MergeKey(value.Object, value.PropertyPath, type);
                return true;
            case UpdateSplineEasingOperation spline:
                key = new MergeKey(spline.Easing, spline.PropertyPath, type);
                return true;
            case UpdateNodeMemberOperation node:
                key = new MergeKey(node.NodeMember, node.PropertyPath, type);
                return true;
            default:
                key = default;
                return false;
        }
    }

    private void RebuildMergeCandidates()
    {
        _mergeCandidates.Clear();
        for (int i = 0; i < _operations.Count; i++)
            if (TryGetMergeKey(_operations[i], out MergeKey key)) _mergeCandidates[key] = i;
    }

    private readonly record struct MergeKey(object Owner, string PropertyPath, Type OperationType)
    {
        public bool Equals(MergeKey other) => ReferenceEquals(Owner, other.Owner)
            && PropertyPath == other.PropertyPath && OperationType == other.OperationType;

        public override int GetHashCode()
            => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Owner), PropertyPath, OperationType);
    }

    private void CompactOperations()
    {
        // Earlier pairs were compacted when recorded. Only the new operation (and an
        // operation changed by merging it) can need another comparison.
        int i = _operations.Count - 1;
        while (i > 0)
        {
            int mergedIndex = -1;
            for (int j = i - 1; j >= 0; j--)
            {
                if (_applied[i] == _applied[j]
                    && _operations[j] is IMergableChangeOperation mergableChangeOperation
                    && mergableChangeOperation.TryMerge(_operations[i]))
                {
                    _operations.RemoveAt(i);
                    _applied.RemoveAt(i);
                    // Writes that returned to the starting value leave nothing to undo. Keeping
                    // them would turn the next commit, from any feature, into an empty entry.
                    if (mergableChangeOperation.IsNoop)
                    {
                        _operations.RemoveAt(j);
                        _applied.RemoveAt(j);
                        return;
                    }
                    mergedIndex = j;
                    break;
                }
            }
            i = mergedIndex;
        }
    }

    internal void Apply()
    {
        ThrowIfUncertainFailure();
        for (int i = 0; i < _operations.Count; i++)
        {
            if (_applied[i]) continue;
            ExecuteOperation(i, apply: true);
        }
    }

    internal void Revert()
    {
        ThrowIfUncertainFailure();
        for (int i = _operations.Count - 1; i >= 0; i--)
        {
            if (!_applied[i]) continue;
            ExecuteOperation(i, apply: false);
        }
    }

    private void ThrowIfUncertainFailure()
    {
        if (HasUncertainFailure) throw new InvalidOperationException("This transaction has uncertain partial execution and cannot be replayed.");
    }
}
