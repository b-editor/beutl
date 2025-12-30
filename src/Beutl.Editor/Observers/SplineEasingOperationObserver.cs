using System.Reactive.Subjects;

using Beutl.Animation.Easings;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;

namespace Beutl.Editor.Observers;

public sealed class SplineEasingOperationObserver : IOperationObserver
{
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly SplineEasing _easing;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly CoreObject? _parent;
    private readonly string _propertyPath;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly HashSet<string>? _propertiesToTrack;

    private float _x1;
    private float _y1;
    private float _x2;
    private float _y2;

    public SplineEasingOperationObserver(
        IObserver<ChangeOperation>? observer,
        SplineEasing easing,
        OperationSequenceGenerator sequenceNumberGenerator,
        CoreObject? parent,
        string propertyPath = "",
        HashSet<string>? propertyPathsToTrack = null)
    {
        _easing = easing;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _parent = parent;
        _propertyPath = propertyPath;
        _propertyPathsToTrack = propertyPathsToTrack;

        _x1 = easing.X1;
        _y1 = easing.Y1;
        _x2 = easing.X2;
        _y2 = easing.Y2;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        _propertiesToTrack = _propertyPathsToTrack?.Where(i => i.Contains(_propertyPath))
            .Select(i => i.Substring(_propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();

        _easing.Changed += OnEasingChanged;
    }

    public IObservable<ChangeOperation> Operations => _operations;

    public void Dispose()
    {
        _easing.Changed -= OnEasingChanged;
        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private void OnEasingChanged(object? sender, EventArgs e)
    {
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        if (_easing.X1 != _x1 && ShouldTrack(nameof(SplineEasing.X1)))
        {
            PublishChange(nameof(SplineEasing.X1), _easing.X1, _x1);
            _x1 = _easing.X1;
        }

        if (_easing.Y1 != _y1 && ShouldTrack(nameof(SplineEasing.Y1)))
        {
            PublishChange(nameof(SplineEasing.Y1), _easing.Y1, _y1);
            _y1 = _easing.Y1;
        }

        if (_easing.X2 != _x2 && ShouldTrack(nameof(SplineEasing.X2)))
        {
            PublishChange(nameof(SplineEasing.X2), _easing.X2, _x2);
            _x2 = _easing.X2;
        }

        if (_easing.Y2 != _y2 && ShouldTrack(nameof(SplineEasing.Y2)))
        {
            PublishChange(nameof(SplineEasing.Y2), _easing.Y2, _y2);
            _y2 = _easing.Y2;
        }
    }

    private bool ShouldTrack(string propertyName)
    {
        return _propertiesToTrack?.Contains(propertyName) != false;
    }

    private void PublishChange(string propertyName, float newValue, float oldValue)
    {
        string fullPath = string.IsNullOrEmpty(_propertyPath)
            ? propertyName
            : $"{_propertyPath}.{propertyName}";

        var operation = new UpdateSplineEasingOperation(_easing, fullPath, newValue, oldValue)
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext(),
            Parent = _parent
        };
        _operations.OnNext(operation);
    }
}
