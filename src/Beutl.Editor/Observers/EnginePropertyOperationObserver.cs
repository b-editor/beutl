using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.Editor.Observers;

public sealed class EnginePropertyOperationObserver<T> : IOperationObserver
{
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly ICoreObject _object;
    private readonly IProperty<T> _property;
    private readonly IExpressionProperty<T>? _expressionProperty;
    private readonly string _propertyPath;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private IOperationObserver? _valuePublisher;
    private IOperationObserver? _animationPublisher;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly bool _tracksAnimation;
    private KeyFrameAnimation<T>? _animation;
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
        _expressionProperty = property as IExpressionProperty<T>;
        _propertyPath = propertyPath;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _propertyPathsToTrack = propertyPathsToTrack;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        HashSet<string>? propertiesToTrack =
            OperationObserverHelpers.GetTrackedChildNames(_propertyPathsToTrack, _propertyPath);

        if (propertiesToTrack?.Contains("CurrentValue") != false)
        {
            InitializePublishers(property.Value);

            _property.ValueChanged += OnValueChanged;
        }

        // Keyframes are the bottom of the expression chain; their own edits keep the "Animation" path.
        _tracksAnimation = _expressionProperty?.IsAnimatable == true && propertiesToTrack?.Contains("Animation") != false;
        if (_tracksAnimation)
        {
            ObserveAnimation(_property.GetKeyFrameAnimation(), null);
        }

        if (_expressionProperty != null
            && (propertiesToTrack?.Contains("Expression") != false || _tracksAnimation))
        {
            _expression = _expressionProperty.Expression;
            _expressionProperty.ExpressionChanged += OnExpressionChanged;
        }
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _property.ValueChanged -= OnValueChanged;
        _animation = null;
        if (_expressionProperty != null)
        {
            _expression = null;
            _expressionProperty.ExpressionChanged -= OnExpressionChanged;
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
            _valuePublisher = OperationObserverHelpers.CreateCollectionObserver(
                _operations, list, _object, _propertyPath, _sequenceNumberGenerator, _propertyPathsToTrack);
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
        // KeyFrameAnimation<T> re-sorts its key frames under PublishingSuppression; that is not an edit.
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        if (_tracksAnimation)
        {
            ObserveAnimation(_property.GetKeyFrameAnimation(), _propertyPathsToTrack);
        }

        var operation = new UpdatePropertyValueOperation<IExpression<T>?>(
            (CoreObject)_object, $"{_propertyPath}.Expression", obj, _expression)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext()
        };
        _operations.OnNext(operation);
        _expression = obj;
    }

    private void ObserveAnimation(KeyFrameAnimation<T>? animation, HashSet<string>? propertyPathsToTrack)
    {
        if (ReferenceEquals(animation, _animation) && (_animationPublisher != null || animation == null)) return;

        _animationPublisher?.Dispose();
        _animationPublisher = animation == null
            ? null
            : new CoreObjectOperationObserver(
                _operations,
                animation,
                _sequenceNumberGenerator,
                $"{_propertyPath}.Animation",
                propertyPathsToTrack);
        _animation = animation;
    }

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T> e)
    {
        // KeyFrameAnimation<T> re-sorts its key frames under PublishingSuppression; that is not an edit.
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        _valuePublisher?.Dispose();
        _valuePublisher = null;

        InitializePublishers(e.NewValue);

        var operation = new UpdatePropertyValueOperation<T>(
            (CoreObject)_object, _propertyPath, e.NewValue, e.OldValue)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext()
        };
        _operations.OnNext(operation);
    }
}
