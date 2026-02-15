using System.Collections;
using System.ComponentModel;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.NodeTree;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class CoreObjectOperationObserver : IOperationObserver
{
    private readonly ICoreObject _object;
    private readonly string _propertyPath;
    private readonly Dictionary<int, IOperationObserver> _corePropertyPublishers = new();
    private readonly Dictionary<string, IOperationObserver> _enginePropertyPublishers = new();
    private SplineEasingOperationObserver? _splineEasingPublisher;
    private NodeItemOperationObserver? _nodeItemPublisher;
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

        _propertiesToTrack = _propertyPathsToTrack?.Where(i => i.Contains(_propertyPath))
            .Select(i => i.Substring(_propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();
        InitializeChildPublishers();

        // Easingの変更を監視
        if (obj is KeyFrame)
        {
            obj.PropertyChanged += OnPropertyChanged;
        }

        if (obj is EngineObject engineObject)
        {
            InitializeEnginePropertyPublishers(engineObject);
        }
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _object.PropertyChanged -= OnPropertyChanged;

        foreach (IOperationObserver collection in _corePropertyPublishers.Values)
        {
            collection.Dispose();
        }

        foreach (IOperationObserver enginePublisher in _enginePropertyPublishers.Values)
        {
            enginePublisher.Dispose();
        }

        _splineEasingPublisher?.Dispose();
        _nodeItemPublisher?.Dispose();

        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private string BuildPropertyPath(string propertyName)
    {
        return string.IsNullOrEmpty(_propertyPath)
            ? propertyName
            : $"{_propertyPath}.{propertyName}";
    }

    private void InitializeChildPublishers()
    {
        var objectType = _object.GetType();
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(objectType))
        {
            if (Hierarchical.HierarchicalParentProperty.Id == property.Id) continue;
            if (_propertiesToTrack != null && !_propertiesToTrack.Contains(property.Name))
            {
                continue;
            }

            // Check if property is excluded from tracking
            if (property.TryGetMetadata<CorePropertyMetadata>(objectType, out var metadata)
                && !metadata.Tracked)
            {
                continue;
            }

            string childPath = BuildPropertyPath(property.Name);

            var observerType = typeof(CorePropertyOperationObserver<>).MakeGenericType(property.PropertyType);
            var propertyPublisher = (IOperationObserver)Activator.CreateInstance(
                observerType,
                _operations,
                _object,
                property,
                _sequenceNumberGenerator,
                childPath,
                _propertyPathsToTrack)!;

            _corePropertyPublishers.Add(property.Id, propertyPublisher);
        }

        RecreateSplineEasingPublisher();
        InitializeNodeItemPublisher();
    }

    private void RecreateSplineEasingPublisher()
    {
        _splineEasingPublisher?.Dispose();
        _splineEasingPublisher = null;

        if (_object is KeyFrame { Easing: SplineEasing splineEasing } keyFrame)
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

    private void InitializeNodeItemPublisher()
    {
        // IEnginePropertyBackedInputSocketの場合はEnginePropertyOperationObserverで監視するため、NodeItemOperationObserverは作成しない
        if (_object is INodeItem nodeItem and not IEnginePropertyBackedInputSocket)
        {
            _nodeItemPublisher = new NodeItemOperationObserver(
                _operations,
                nodeItem,
                _sequenceNumberGenerator,
                _propertyPath,
                _propertyPathsToTrack);
        }
    }

    private void InitializeEnginePropertyPublishers(EngineObject engineObject)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            if (_propertiesToTrack != null && !_propertiesToTrack.Contains(property.Name))
            {
                continue;
            }

            string childPath = BuildPropertyPath(property.Name);
            var publisherType = typeof(EnginePropertyOperationObserver<>).MakeGenericType(property.ValueType);
            var publisher = (IOperationObserver)Activator.CreateInstance(
                publisherType,
                _operations,
                engineObject,
                property,
                _sequenceNumberGenerator,
                childPath,
                _propertyPathsToTrack)!;

            _enginePropertyPublishers.Add(property.Name, publisher);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (PublishingSuppression.IsSuppressed) return;

        if (e is not CorePropertyChangedEventArgs<Easing> args || sender is not CoreObject source)
            return;

        if (args.Property.Id != KeyFrame.EasingProperty.Id)
            return;

        if (_propertiesToTrack != null &&
            !_propertiesToTrack.Contains(args.Property.Name))
            return;

        RecreateSplineEasingPublisher();
    }
}
