using System.Collections;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Editor.Operations;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class NodeMemberOperationObserver : IOperationObserver
{
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly INodeMember _nodeMember;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly string _propertyPath;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly CompositeDisposable _subscriptionToProperty = [];
    private IOperationObserver? _valueObserver;
    private IOperationObserver? _animationObserver;

    public NodeMemberOperationObserver(
        IObserver<ChangeOperation>? observer,
        INodeMember nodeMember,
        OperationSequenceGenerator sequenceNumberGenerator,
        string propertyPath = "",
        HashSet<string>? propertyPathsToTrack = null)
    {
        _nodeMember = nodeMember;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _propertyPath = propertyPath;
        _propertyPathsToTrack = propertyPathsToTrack;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        HashSet<string>? propertiesToTrack = _propertyPathsToTrack?.Where(i => i.Contains(_propertyPath))
            .Select(i => i.Substring(_propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();

        if (propertiesToTrack?.Contains("Property") != false)
        {
            RecreateChildObserver(_nodeMember.Property?.GetValue());
            _nodeMember.Property?.GetObservable()
                .CombineWithPrevious()
                .Skip(1)
                .Subscribe(t => OnChanged(t.OldValue, t.NewValue))
                .DisposeWith(_subscriptionToProperty);
        }

        if (_nodeMember.Property is IAnimatablePropertyAdapter animatablePropertyAdapter &&
            propertiesToTrack?.Contains("Animation") != false)
        {
            RecreateAnimationObserver(animatablePropertyAdapter.Animation);
            animatablePropertyAdapter.ObserveAnimation
                .CombineWithPrevious()
                .Skip(1)
                .Subscribe(t => OnAnimationChanged(t.OldValue, t.NewValue))
                .DisposeWith(_subscriptionToProperty);
        }

        if (_nodeMember.Property is IExpressionPropertyAdapter expressionPropertyAdapter &&
            propertiesToTrack?.Contains("Expression") != false)
        {
            expressionPropertyAdapter.ObserveExpression
                .CombineWithPrevious()
                .Skip(1)
                .Subscribe(OnExpressionChanged)
                .DisposeWith(_subscriptionToProperty);
        }
    }

    private void OnExpressionChanged((IExpression? OldValue, IExpression? NewValue) t)
    {
        if (PublishingSuppression.IsSuppressed) return;

        var operation =
            new UpdateNodeMemberOperation(_nodeMember,
                string.IsNullOrEmpty(_propertyPath) ? "Expression" : $"{_propertyPath}.Expression", t.NewValue,
                t.OldValue)
            { SequenceNumber = _sequenceNumberGenerator.GetNext() };
        _operations.OnNext(operation);
    }

    private string GetAnimationPath()
    {
        return string.IsNullOrEmpty(_propertyPath) ? "Animation" : $"{_propertyPath}.Animation";
    }

    private void RecreateAnimationObserver(IAnimation? animation)
    {
        _animationObserver?.Dispose();
        _animationObserver = null;

        if (animation is ICoreObject animObject)
        {
            _animationObserver = new CoreObjectOperationObserver(
                _operations,
                animObject,
                _sequenceNumberGenerator,
                GetAnimationPath(),
                _propertyPathsToTrack);
        }
    }

    private void OnAnimationChanged(IAnimation? oldAnimation, IAnimation? newAnimation)
    {
        if (PublishingSuppression.IsSuppressed) return;

        RecreateAnimationObserver(newAnimation);

        var operation = new UpdateNodeMemberOperation(
            _nodeMember, GetAnimationPath(), newAnimation, oldAnimation)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext()
        };
        _operations.OnNext(operation);
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _subscriptionToProperty?.Dispose();
        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private void RecreateChildObserver(object? value)
    {
        _valueObserver?.Dispose();
        _valueObserver = null;
        switch (value)
        {
            case CoreObject coreObject:
                _valueObserver = new CoreObjectOperationObserver(
                    _operations,
                    coreObject,
                    _sequenceNumberGenerator,
                    string.IsNullOrEmpty(_propertyPath) ? "Property" : $"{_propertyPath}.Property",
                    _propertyPathsToTrack);
                break;
            case IList list:
                var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
                if (elementType == null)
                    throw new InvalidOperationException("Could not determine the element type of the list.");
                var observerType = typeof(CollectionOperationObserver<>).MakeGenericType(elementType);

                _valueObserver = (IOperationObserver?)Activator.CreateInstance(observerType,
                    _operations, list, _nodeMember,
                    string.IsNullOrEmpty(_propertyPath) ? "Property" : $"{_propertyPath}.Property",
                    _sequenceNumberGenerator,
                    _propertyPathsToTrack)!;
                break;
        }
    }

    private void OnChanged(object? oldValue, object? newValue)
    {
        if (PublishingSuppression.IsSuppressed) return;

        RecreateChildObserver(newValue);

        PublishValueChange(newValue, oldValue);
    }

    private void PublishValueChange(object? newValue, object? oldValue)
    {
        string fullPath = string.IsNullOrEmpty(_propertyPath)
            ? "Property"
            : $"{_propertyPath}.Property";

        var operation = new UpdateNodeMemberOperation(_nodeMember, fullPath, newValue, oldValue)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext()
        };
        _operations.OnNext(operation);
    }
}
