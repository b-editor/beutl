using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Property;

namespace Beutl.Protocol.Synchronization;

// TODO: IListProperty対応
public sealed class EnginePropertyOperationPublisher<T> : IOperationPublisher
{
    private readonly Subject<SyncOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly ICoreObject _object;
    private readonly IProperty<T> _property;
    private readonly string _propertyPath;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private IOperationPublisher? _valuePublisher;
    private IOperationPublisher? _animationPublisher;

    public EnginePropertyOperationPublisher(
        IObserver<SyncOperation>? observer,
        ICoreObject obj,
        IProperty<T> property,
        OperationSequenceGenerator sequenceNumberGenerator,
        string propertyPath)
    {
        _object = obj;
        _property = property;
        _propertyPath = propertyPath;
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
                    _sequenceNumberGenerator,
                    $"{_propertyPath}.Animation");
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
                _propertyPath,
                _sequenceNumberGenerator);
        }
        else if (value is ICoreObject child)
        {
            _valuePublisher = new CoreObjectOperationPublisher(
                _operations,
                child,
                _sequenceNumberGenerator,
                _propertyPath);
        }
    }

    private void OnAnimationChanged(IAnimation<T>? animation)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        _animationPublisher?.Dispose();
        _animationPublisher = null;

        string animationPath = $"{_propertyPath}.Animation";

        if (animation is ICoreObject animObject)
        {
            _animationPublisher = new CoreObjectOperationPublisher(
                _operations,
                animObject,
                _sequenceNumberGenerator,
                animationPath);
        }

        _operations.OnNext(UpdatePropertyValueOperation.Create(
            _object,
            _property,
            animationPath,
            typeof(IAnimation),
            animation,
            _sequenceNumberGenerator));
    }

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T> e)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        _valuePublisher?.Dispose();
        _valuePublisher = null;

        InitializePublishers(e.NewValue);

        _operations.OnNext(UpdatePropertyValueOperation.Create(
            _object,
            _property,
            _propertyPath,
            _property.ValueType,
            e.NewValue,
            _sequenceNumberGenerator));
    }
}
