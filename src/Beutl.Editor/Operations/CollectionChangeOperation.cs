using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Editor.Infrastructure;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.NodeGraph;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Operations;

public abstract class CollectionChangeOperation<T> : ChangeOperation, IPropertyPathProvider
{
    private static readonly ILogger s_logger = Log.CreateLogger<CollectionChangeOperation<T>>();
    private ChangeOperationFailureState _failureState;
    private string? _relocation;
    public override ChangeOperationFailureState FailureState => _failureState;

    private void ExecuteWithFailureState(IList<T> list, bool revert, Action mutation)
    {
        _failureState = ChangeOperationFailureState.Unknown;
        _relocation = null;
        if (this is not MoveCollectionRangeOperation<T>)
        {
            mutation();
            LogRelocation();
            return;
        }

        // The sealed move implementation can be evaluated on a private List first.
        // Invalid/stale indices therefore fail before removing anything from the live list.
        _failureState = ChangeOperationFailureState.Unchanged;
        T[] before = list.ToArray();
        var expected = new List<T>(before);
        if (revert) RevertTo(expected); else ApplyTo(expected);
        _failureState = ChangeOperationFailureState.Unknown;
        _relocation = null;
        try
        {
            mutation();
            LogRelocation();
        }
        catch
        {
            // A collection observer may throw after the move has fully committed.
            // Record that result so history advances past it rather than moving again.
            try
            {
                if (Matches(list, before)) _failureState = ChangeOperationFailureState.Unchanged;
                else if (Matches(list, expected)) _failureState = ChangeOperationFailureState.Completed;
            }
            catch { }
            throw;
        }
    }

    private void AddRelocation(string relocation)
        => _relocation = _relocation is null ? relocation : $"{_relocation}, {relocation}";

    private void LogRelocation()
    {
        if (_relocation is { } relocation)
            s_logger.LogWarning("Replay of {PropertyPath} {Relocation}.", PropertyPath, relocation);
    }

    private static bool IsSame(T left, T right)
        => typeof(T).IsValueType ? EqualityComparer<T>.Default.Equals(left, right) : ReferenceEquals(left, right);

    private static bool IsAt(IList<T> list, int index, T[] items)
    {
        if (index < 0 || index > list.Count - items.Length) return false;
        for (int i = 0; i < items.Length; i++)
        {
            if (!IsSame(list[index + i], items[i])) return false;
        }
        return true;
    }

    // A change that history did not record can shift items, so replay removes them where they are now.
    private protected void RemoveItems(IList<T> list, int index, T[] items, Action<int, int>? removeRange = null)
    {
        if (items.Length == 0) return;
        if (IsAt(list, index, items))
        {
            if (removeRange != null)
            {
                removeRange(index, items.Length);
            }
            else
            {
                for (int i = 0; i < items.Length; i++) list.RemoveAt(index);
            }
            return;
        }

        var found = new SortedSet<int>();
        foreach (T item in items)
        {
            int at = Enumerable.Range(0, list.Count).FirstOrDefault(i => !found.Contains(i) && IsSame(list[i], item), -1);
            if (at < 0)
            {
                _failureState = ChangeOperationFailureState.Unchanged;
                throw new InvalidOperationException($"Replay of {PropertyPath} cannot find an item to remove.");
            }
            found.Add(at);
        }

        AddRelocation($"removed {items.Length} item(s) at {string.Join(", ", found)} instead of index {index}");
        foreach (int at in found.Reverse()) list.RemoveAt(at);
    }

    private protected int GetInsertionIndex(IList<T> list, int index)
    {
        if (index >= 0 && index <= list.Count) return index;
        AddRelocation($"inserted at {list.Count} instead of index {index}");
        return list.Count;
    }

    // Where a block of count items ends up, kept within the list's length.
    private protected int GetMoveDestination(IList<T> list, int index, int count)
    {
        int last = Math.Max(list.Count - count, 0);
        if (index >= 0 && index <= last) return index;
        int destination = Math.Clamp(index, 0, last);
        AddRelocation($"moved to {destination} instead of index {index}");
        return destination;
    }

    private protected int FindBlock(IList<T> list, int index, T[] items)
    {
        if (items.Length == 0 || IsAt(list, index, items)) return index;
        int at = Enumerable.Range(0, list.Count).FirstOrDefault(i => IsAt(list, i, items), -1);
        if (at < 0)
        {
            _failureState = ChangeOperationFailureState.Unchanged;
            throw new InvalidOperationException($"Replay of {PropertyPath} cannot find the items to move.");
        }

        AddRelocation($"moved {items.Length} item(s) from {at} instead of index {index}");
        return at;
    }

    private static bool Matches(IList<T> actual, IList<T> expected)
    {
        if (actual.Count != expected.Count) return false;
        for (int i = 0; i < actual.Count; i++)
        {
            if (!typeof(T).IsValueType)
            {
                if (!ReferenceEquals(actual[i], expected[i])) return false;
            }
            else
            {
                // Custom equality can ignore state (and floating-point equality hides
                // signed zero). Require exact bytes, or conservatively report unknown.
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return false;
                T left = actual[i];
                T right = expected[i];
                ReadOnlySpan<byte> leftBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref left), Unsafe.SizeOf<T>());
                ReadOnlySpan<byte> rightBytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref right), Unsafe.SizeOf<T>());
                if (!leftBytes.SequenceEqual(rightBytes)) return false;
            }
        }
        return true;
    }

    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    private IList<T> VerifyType(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        return list2;
    }

    private IProperty FindListProperty(EngineObject engineObj, string name)
    {
        var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                             ?? throw new InvalidOperationException(
                                 $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

        // A value-only property that holds its object's list, like GraphModel.Nodes, changes that list.
        if (engineProperty is not IListProperty<T>
            && (engineProperty is IExpressionProperty || engineProperty.Value is not IList<T>))
        {
            throw new InvalidOperationException(
                $"Engine property {PropertyPath} is not a list on type {engineObj.GetType().FullName}.");
        }

        return engineProperty;
    }

    private void Execute(bool revert)
    {
        _failureState = ChangeOperationFailureState.Unknown;
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            IList<T> list = VerifyType(Object, Object.GetValue(coreProperty));
            ExecuteWithFailureState(list, revert, revert ? () => RevertTo(list) : () => ApplyTo(list));
            return;
        }

        if (Object is INodeMember nodeMember && name == "Property")
        {
            IList<T> list = VerifyType(nodeMember, nodeMember.Property?.GetValue());
            ExecuteWithFailureState(list, revert, revert ? () => RevertTo(list) : () => ApplyTo(list));
            return;
        }

        if (Object is EngineObject engineObj)
        {
            IProperty engineProperty = FindListProperty(engineObj, name);
            if (engineProperty is IListProperty<T> listProperty)
            {
                ExecuteWithFailureState(listProperty, revert, revert
                    ? () => RevertToEngineProperty(listProperty)
                    : () => ApplyToEngineProperty(listProperty));
                return;
            }

            IList<T> list = (IList<T>)engineProperty.Value!;
            ExecuteWithFailureState(list, revert, revert ? () => RevertTo(list) : () => ApplyTo(list));
        }
    }

    public override void Apply() => Execute(revert: false);

    protected virtual void ApplyToEngineProperty(IListProperty<T> listProperty) => ApplyTo(listProperty);

    protected abstract void ApplyTo(IList<T> list);

    public override void Revert() => Execute(revert: true);

    protected virtual void RevertToEngineProperty(IListProperty<T> listProperty) => RevertTo(listProperty);

    protected abstract void RevertTo(IList<T> list);
}
