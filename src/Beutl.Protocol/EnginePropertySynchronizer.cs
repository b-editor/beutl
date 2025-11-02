using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.Protocol;

public class EnginePropertySynchronizer<T> : ISynchronizer
{
    private readonly Subject<OperationBase> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly ICoreObject _obj;
    private readonly IProperty<T> _property;
    private ISynchronizer? _valueSynchronizer;
    private ISynchronizer? _animationSynchronizer;
    private readonly SequenceNumberGenerator _sequenceNumberGenerator;

    public EnginePropertySynchronizer(
        IObserver<OperationBase>? observer,
        ICoreObject obj,
        IProperty<T> property,
        SequenceNumberGenerator sequenceNumberGenerator)
    {
        _obj = obj;
        _property = property;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        var value = property.CurrentValue;
        if (value is IList list)
        {
            _valueSynchronizer = new ListSynchronizer(_operations, list, obj, property.Name, _sequenceNumberGenerator);
        }
        else if (value is ICoreObject childObj)
        {
            _valueSynchronizer = new ObjectSynchronizer(_operations, childObj, _sequenceNumberGenerator);
        }

        property.ValueChanged += OnValueChanged;
        if (property is AnimatableProperty<T> animProp)
        {
            if (animProp.Animation is ICoreObject animObj)
            {
                _animationSynchronizer = new ObjectSynchronizer(_operations, animObj, _sequenceNumberGenerator);
            }

            animProp.AnimationChanged += OnAnimationChanged;
        }
    }

    private void OnAnimationChanged(IAnimation<T>? animation)
    {
        _animationSynchronizer?.Dispose();
        _animationSynchronizer = null;

        if (animation is ICoreObject animObj)
        {
            _animationSynchronizer = new ObjectSynchronizer(_operations, animObj, _sequenceNumberGenerator);
        }

        _operations.OnNext(UpdatePropertyOperation.Create(_obj, _property, $"{_property.Name}.Animation", typeof(IAnimation), animation, _sequenceNumberGenerator));
    }

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T> e)
    {
        _valueSynchronizer?.Dispose();
        _valueSynchronizer = null;

        if (e.NewValue is IList list)
        {
            _valueSynchronizer = new ListSynchronizer(_operations, list, _obj, _property.Name, _sequenceNumberGenerator);
        }
        else if (e.NewValue is ICoreObject childObj)
        {
            _valueSynchronizer = new ObjectSynchronizer(_operations, childObj, _sequenceNumberGenerator);
        }

        _operations.OnNext(UpdatePropertyOperation.Create(_obj, _property, _property.Name, _property.ValueType, e.NewValue, _sequenceNumberGenerator));
    }

    public IObservable<OperationBase> Operations => _operations;

    public void Dispose()
    {
        _property.ValueChanged -= OnValueChanged;
        if (_property is AnimatableProperty<T> animProp)
        {
            animProp.AnimationChanged -= OnAnimationChanged;
        }
        _valueSynchronizer?.Dispose();
        _animationSynchronizer?.Dispose();
        _subscription?.Dispose();
        _operations.OnCompleted();
    }
}
