using Beutl.Animation;
using Beutl.Extensibility;

namespace Beutl.NodeTree.Rendering;

public delegate bool ItemValueReceiver<T>(IItemValue obj, out T? received);

public interface IReadOnlyItemValue<out T> : IItemValue
{
    T? GetValue();
}

public sealed class ItemValue<T> : IReadOnlyItemValue<T>
{
    private ItemValueReceiver<T>? _receiver;
    private Action? _disposer;
    public T? Value;

    T? IReadOnlyItemValue<T>.GetValue() => Value;

    public void RegisterReceiver(ItemValueReceiver<T> receiver)
    {
        _receiver = receiver;
    }

    public void RegisterDisposer(Action disposer)
    {
        _disposer = disposer;
    }

    public PropagateResult PropagateFrom(IItemValue source)
    {
        if (source is IReadOnlyItemValue<T> typed)
        {
            Value = typed.GetValue();
            return PropagateResult.Success;
        }

        if (_receiver?.Invoke(source, out T? received) == true)
        {
            Value = received;
            return PropagateResult.Converted;
        }

        return PropagateResult.Failed;
    }

    public bool TryCopyFrom(IPropertyAdapter source)
    {
        if (source is IPropertyAdapter<T> typed)
        {
            Value = typed.GetValue();
            return true;
        }

        return false;
    }

    public void SetFromObject(object? value) => Value = (T?)value;

    public object? GetBoxed() => Value;

    public bool TryLoadFromAnimation(IAnimation animation, TimeSpan time)
    {
        if (animation is IAnimation<T> typed)
        {
            Value = typed.GetAnimatedValue(time);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _disposer?.Invoke();
        _disposer = null;
    }
}
