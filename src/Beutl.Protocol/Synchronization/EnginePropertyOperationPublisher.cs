using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Collections;
using Beutl.Protocol.Operations.Property;

namespace Beutl.Protocol.Synchronization;

public sealed class EnginePropertyOperationPublisher<T> : IOperationPublisher
{
    private readonly Subject<SyncOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly ICoreObject _object;
    private readonly IProperty<T> _property;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private IOperationPublisher? _valuePublisher;
    private IOperationPublisher? _animationPublisher;

    public EnginePropertyOperationPublisher(
        IObserver<SyncOperation>? observer,
        ICoreObject obj,
        IProperty<T> property,
        OperationSequenceGenerator sequenceNumberGenerator)
    {
        _object = obj;
        _property = property;
        _sequenceNumberGenerator = sequenceNumberGenerator;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        InitializePublishers(property.CurrentValue);

        _property.ValueChanged += OnValueChanged;
        if (_property is AnimatableProperty<T> animatable)
        {
            if (animatable.Animation is ICoreObject animationObject)
            {
                _animationPublisher = new CoreObjectOperationPublisher(
                    _operations,
                    animationObject,
                    _sequenceNumberGenerator);
            }

            animatable.AnimationChanged += OnAnimationChanged;
        }
    }

    public IObservable<SyncOperation> Operations => _operations;

    public void Dispose()
    {
        _property.ValueChanged -= OnValueChanged;
        if (_property is AnimatableProperty<T> animatable)
        {
            animatable.AnimationChanged -= OnAnimationChanged;
        }

        _valuePublisher?.Dispose();
        _animationPublisher?.Dispose();
        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private void InitializePublishers(object? value)
    {
        if (value is IList list)
        {
            _valuePublisher = new CollectionOperationPublisher(
                _operations,
                list,
                _object,
                _property.Name,
                _sequenceNumberGenerator);
        }
        else if (value is ICoreObject child)
        {
            _valuePublisher = new CoreObjectOperationPublisher(
                _operations,
                child,
                _sequenceNumberGenerator);
        }
    }

    private void OnAnimationChanged(IAnimation<T>? animation)
    {
        _animationPublisher?.Dispose();
        _animationPublisher = null;

        if (animation is ICoreObject animObject)
        {
            _animationPublisher = new CoreObjectOperationPublisher(
                _operations,
                animObject,
                _sequenceNumberGenerator);
        }

        _operations.OnNext(UpdatePropertyValueOperation.Create(
            _object,
            _property,
            $"{_property.Name}.Animation",
            typeof(IAnimation),
            animation,
            _sequenceNumberGenerator));
    }

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T> e)
    {
        _valuePublisher?.Dispose();
        _valuePublisher = null;

        InitializePublishers(e.NewValue);

        _operations.OnNext(UpdatePropertyValueOperation.Create(
            _object,
            _property,
            _property.Name,
            _property.ValueType,
            e.NewValue,
            _sequenceNumberGenerator));
    }
}
