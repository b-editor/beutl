using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class EnginePropertyOperationObserver<T> : IOperationObserver
{
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly ICoreObject _object;
    private readonly IProperty<T> _property;
    private readonly string _propertyPath;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private IOperationObserver? _valuePublisher;
    private IOperationObserver? _animationPublisher;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private IAnimation<T>? _animation;
    private IExpression<T>? _expression;

    public EnginePropertyOperationObserver(
        IObserver<ChangeOperation>? observer,
        ICoreObject obj,
        IProperty<T> property,
        OperationSequenceGenerator sequenceNumberGenerator,
        string propertyPath,
        HashSet<string>? propertyPathsToTrack = null)
    {
        _object = obj;
        _property = property;
        _propertyPath = propertyPath;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _propertyPathsToTrack = propertyPathsToTrack;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        HashSet<string>? propertiesToTrack = _propertyPathsToTrack?.Where(i => i.Contains(_propertyPath))
            .Select(i => i.Substring(_propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();

        if (propertiesToTrack?.Contains("CurrentValue") != false)
        {
            InitializePublishers(property.CurrentValue);

            _property.ValueChanged += OnValueChanged;
        }

        if (_property is AnimatableProperty<T> animatable)
        {
            if (propertiesToTrack?.Contains("Animation") != false)
            {
                if (animatable.Animation is ICoreObject animationObject)
                {
                    _animationPublisher = new CoreObjectOperationObserver(
                        _operations,
                        animationObject,
                        _sequenceNumberGenerator,
                        $"{_propertyPath}.Animation");
                }

                _animation = animatable.Animation;
                animatable.AnimationChanged += OnAnimationChanged;
            }

            if (propertiesToTrack?.Contains("Expression") != false)
            {
                _expression = animatable.Expression;
                animatable.ExpressionChanged += OnExpressionChanged;
            }
        }
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _property.ValueChanged -= OnValueChanged;
        if (_property is AnimatableProperty<T> animatable)
        {
            _animation = null;
            _expression = null;
            animatable.ExpressionChanged -= OnExpressionChanged;
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
            var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
            if (elementType == null) throw new InvalidOperationException("Could not determine the element type of the list.");
            var observerType = typeof(CollectionOperationObserver<>).MakeGenericType(elementType);

            _valuePublisher = (IOperationObserver)Activator.CreateInstance(observerType,
                _operations, list, _object,
                _propertyPath, _sequenceNumberGenerator, _propertyPathsToTrack)!;
        }
        else if (value is ICoreObject child)
        {
            _valuePublisher = new CoreObjectOperationObserver(
                _operations,
                child,
                _sequenceNumberGenerator,
                _propertyPath,
                _propertyPathsToTrack);
        }
    }

    private void OnExpressionChanged(IExpression<T>? obj)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        var operationType = typeof(UpdatePropertyValueOperation<>).MakeGenericType(typeof(IExpression<T>));
        var operation = (ChangeOperation)Activator.CreateInstance(
            operationType,
            _object, $"{_propertyPath}.Expression", obj, _expression)!;
        operation.SequenceNumber = _sequenceNumberGenerator.GetNext();
        _operations.OnNext(operation);
        _expression = obj;
    }

    private void OnAnimationChanged(IAnimation<T>? newAnimation)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        _animationPublisher?.Dispose();
        _animationPublisher = null;

        string animationPath = $"{_propertyPath}.Animation";

        if (newAnimation is ICoreObject animObject)
        {
            _animationPublisher = new CoreObjectOperationObserver(
                _operations,
                animObject,
                _sequenceNumberGenerator,
                animationPath,
                _propertyPathsToTrack);
        }

        var operationType = typeof(UpdatePropertyValueOperation<>).MakeGenericType(typeof(IAnimation<T>));
        var operation = (ChangeOperation)Activator.CreateInstance(
            operationType,
            _object, animationPath, newAnimation, _animation)!;
        operation.SequenceNumber = _sequenceNumberGenerator.GetNext();
        _operations.OnNext(operation);
        _animation = newAnimation;
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

        var operationType = typeof(UpdatePropertyValueOperation<>).MakeGenericType(typeof(T));
        var operation = (ChangeOperation)Activator.CreateInstance(
            operationType,
            _object, _propertyPath, e.NewValue, e.OldValue)!;
        operation.SequenceNumber = _sequenceNumberGenerator.GetNext();
        _operations.OnNext(operation);
    }
}
