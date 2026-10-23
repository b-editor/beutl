using System.Collections;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class CoreObjectOperationObserver : IOperationObserver
{
    private readonly ICoreObject _object;
    private readonly string _propertyPath;
    private readonly Dictionary<string, IOperationObserver> _enginePropertyPublishers = new();
    private SplineEasingOperationObserver? _splineEasingPublisher;
    private NodeMemberOperationObserver? _nodeMemberPublisher;
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly HashSet<string>? _propertiesToTrack;

    public CoreObjectOperationObserver(
        IObserver<ChangeOperation>? observer,
        ICoreObject obj,
        OperationSequenceGenerator sequenceNumberGenerator,
        string propertyPath = "",
        HashSet<string>? propertyPathsToTrack = null)
    {
        _object = obj;
        _propertyPath = propertyPath;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _propertyPathsToTrack = propertyPathsToTrack;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        _propertiesToTrack = OperationObserverHelpers.GetTrackedChildNames(_propertyPathsToTrack, _propertyPath);
        InitializeChildPublishers();

        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Easing.ValueChanged += OnEasingChanged;
        }

        if (obj is CoreObject coreObject)
        {
            InitializePropertyPublishers(coreObject);
        }
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        if (_object is KeyFrame keyFrame)
        {
            keyFrame.Easing.ValueChanged -= OnEasingChanged;
        }

        foreach (IOperationObserver enginePublisher in _enginePropertyPublishers.Values)
        {
            enginePublisher.Dispose();
        }

        _splineEasingPublisher?.Dispose();
        _nodeMemberPublisher?.Dispose();

        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private string BuildPropertyPath(string propertyName)
    {
        return OperationObserverHelpers.AppendPath(_propertyPath, propertyName);
    }

    private void InitializeChildPublishers()
    {
        RecreateSplineEasingPublisher();
        InitializeNodeMemberPublisher();
    }

    private void RecreateSplineEasingPublisher()
    {
        _splineEasingPublisher?.Dispose();
        _splineEasingPublisher = null;

        if (_object is KeyFrame { Easing.Value: SplineEasing splineEasing } keyFrame)
        {
            string easingPath = BuildPropertyPath(nameof(KeyFrame.Easing));
            _splineEasingPublisher = new SplineEasingOperationObserver(
                _operations,
                splineEasing,
                _sequenceNumberGenerator,
                keyFrame,
                easingPath,
                _propertyPathsToTrack);
        }
    }

    private void InitializeNodeMemberPublisher()
    {
        // IEnginePropertyBackedInputPortの場合はEnginePropertyOperationObserverで監視するため、NodeMemberOperationObserverは作成しない
        if (_object is INodeMember nodeMember and not IEnginePropertyBackedInputPort)
        {
            _nodeMemberPublisher = new NodeMemberOperationObserver(
                _operations,
                nodeMember,
                _sequenceNumberGenerator,
                _propertyPath,
                _propertyPathsToTrack);
        }
    }

    private void InitializePropertyPublishers(CoreObject coreObject)
    {
        foreach (IProperty property in coreObject.Properties)
        {
            if (_propertiesToTrack != null && !_propertiesToTrack.Contains(property.Name))
            {
                continue;
            }

            // [NotTracked] marks state that is not an edit, such as camera mode's gizmo selection, so changing it
            // records nothing.
            if (property.GetAttributes()?.Any(static attribute => attribute is NotTrackedAttribute) == true)
            {
                continue;
            }

            string childPath = BuildPropertyPath(property.Name);
            var publisherType = typeof(EnginePropertyOperationObserver<>).MakeGenericType(property.ValueType);
            var publisher = (IOperationObserver)Activator.CreateInstance(
                publisherType,
                _operations,
                coreObject,
                property,
                _sequenceNumberGenerator,
                childPath,
                _propertyPathsToTrack)!;

            _enginePropertyPublishers.Add(property.Name, publisher);
        }
    }

    private void OnEasingChanged(object? sender, PropertyValueChangedEventArgs<Easing> e)
    {
        if (PublishingSuppression.IsSuppressed) return;

        if (_propertiesToTrack != null &&
            !_propertiesToTrack.Contains(e.Property.Name))
            return;

        RecreateSplineEasingPublisher();
    }
}
