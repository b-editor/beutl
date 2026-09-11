using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Editor.Infrastructure;
using Beutl.Engine;
using Beutl.NodeGraph;

namespace Beutl.Editor.Operations;

public abstract class CollectionChangeOperation<T> : ChangeOperation, IPropertyPathProvider
{
    private ChangeOperationFailureState _failureState;
    public override ChangeOperationFailureState FailureState => _failureState;

    private void ExecuteWithFailureState(IList<T> list, bool revert, Action mutation)
    {
        _failureState = ChangeOperationFailureState.Unknown;
        if (this is not (MoveCollectionItemOperation<T> or MoveCollectionRangeOperation<T>))
        {
            mutation();
            return;
        }

        // These sealed move implementations can be evaluated on a private List first.
        // Invalid/stale indices therefore fail before removing anything from the live list.
        _failureState = ChangeOperationFailureState.Unchanged;
        T[] before = list.ToArray();
        var expected = new List<T>(before);
        if (revert) RevertTo(expected); else ApplyTo(expected);
        _failureState = ChangeOperationFailureState.Unknown;
        try
        {
            mutation();
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

    private IListProperty<T> FindListProperty(EngineObject engineObj, string name)
    {
        var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                             ?? throw new InvalidOperationException(
                                 $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

        if (engineProperty is not IListProperty<T> listProperty)
        {
            throw new InvalidOperationException(
                $"Engine property {PropertyPath} is not a list on type {engineObj.GetType().FullName}.");
        }

        return listProperty;
    }

    public override void Apply(OperationExecutionContext context)
    {
        _failureState = ChangeOperationFailureState.Unknown;
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            IList<T> list = VerifyType(Object, Object.GetValue(coreProperty));
            ExecuteWithFailureState(list, false, () => ApplyTo(list));
            return;
        }

        if (Object is INodeMember nodeMember && name == "Property")
        {
            IList<T> list = VerifyType(nodeMember, nodeMember.Property?.GetValue());
            ExecuteWithFailureState(list, false, () => ApplyTo(list));
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var listProperty = FindListProperty(engineObj, name);
            ExecuteWithFailureState(listProperty, false, () => ApplyToEngineProperty(listProperty));
        }
    }

    protected abstract void ApplyToEngineProperty(IListProperty<T> listProperty);

    protected abstract void ApplyTo(IList<T> list);

    public override void Revert(OperationExecutionContext context)
    {
        _failureState = ChangeOperationFailureState.Unknown;
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            IList<T> list = VerifyType(Object, Object.GetValue(coreProperty));
            ExecuteWithFailureState(list, true, () => RevertTo(list));
            return;
        }

        if (Object is INodeMember nodeMember && name == "Property")
        {
            IList<T> list = VerifyType(nodeMember, nodeMember.Property?.GetValue());
            ExecuteWithFailureState(list, true, () => RevertTo(list));
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var listProperty = FindListProperty(engineObj, name);
            ExecuteWithFailureState(listProperty, true, () => RevertToEngineProperty(listProperty));
        }
    }

    protected abstract void RevertToEngineProperty(IListProperty<T> listProperty);

    protected abstract void RevertTo(IList<T> list);
}
