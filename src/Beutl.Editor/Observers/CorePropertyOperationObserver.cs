using System.Collections;
using System.ComponentModel;
using System.Reactive.Subjects;
using Beutl.Editor.Operations;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class CorePropertyOperationObserver<T> : IOperationObserver
{
    private CoreObjectOperationObserver? _childPublisher;
    private IOperationObserver? _collectionPublisher;
    private readonly CoreProperty<T> _property;
    private readonly ICoreObject _object;
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly string _propertyPath;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly IDisposable? _subscription;

    public CorePropertyOperationObserver(
        IObserver<ChangeOperation>? observer,
        ICoreObject obj,
        CoreProperty<T> property,
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

        RecreateChildPublisher();
        obj.PropertyChanged += OnPropertyChanged;
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _object.PropertyChanged -= OnPropertyChanged;

        _childPublisher?.Dispose();
        _childPublisher = null;

        _collectionPublisher?.Dispose();
        _collectionPublisher = null;

        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private void RecreateChildPublisher()
    {
        _childPublisher?.Dispose();
        _childPublisher = null;

        _collectionPublisher?.Dispose();
        _collectionPublisher = null;

        T value = _object.GetValue(_property);

        switch (value)
        {
            case IList list:
                var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
                if (elementType == null) throw new InvalidOperationException("Could not determine the element type of the list.");
                var observerType = typeof(CollectionOperationObserver<>).MakeGenericType(elementType);

                _collectionPublisher = (IOperationObserver)Activator.CreateInstance(observerType,
                    _operations, list, _object,
                    _propertyPath, _sequenceNumberGenerator, _propertyPathsToTrack)!;
                break;
            case ICoreObject child:
                _childPublisher = new CoreObjectOperationObserver(
                    _operations,
                    child,
                    _sequenceNumberGenerator,
                    _propertyPath,
                    _propertyPathsToTrack);
                break;
        }
    }

    public void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (PublishingSuppression.IsSuppressed) return;
        if (e is not CorePropertyChangedEventArgs<T> args) return;
        if (sender is not ICoreObject source) return;

        // プロパティが同じか
        if (args.Property.Id != _property.Id) return;

        RecreateChildPublisher();

        var operation = new UpdatePropertyValueOperation<T?>(
            (CoreObject)source, _propertyPath, args.NewValue, args.OldValue)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext()
        };
        _operations.OnNext(operation);
    }
}
