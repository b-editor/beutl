using Avalonia.Threading;
using Beutl.Media;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class EditorClockImpl : IEditorClock, IDisposable
{
    private readonly Scene _scene;
    private readonly ReactivePropertySlim<MediaTime> _currentTime = new();
    private readonly Dictionary<MediaTime, int> _endCounts = [];
    private readonly SortedSet<MediaTime> _ends = [];

    public EditorClockImpl(Scene scene)
    {
        _scene = scene;
        MaximumTime = new ReactivePropertySlim<MediaTime>();

        foreach (Element element in _scene.Children)
        {
            Subscribe(element);
            AddEnd(element.Range.End);
        }

        _scene.Children.Attached += OnElementAttached;
        _scene.Children.Detached += OnElementDetached;

        CalculateMaximumTime();
    }

    public IReadOnlyReactiveProperty<MediaTime> CurrentTime => _currentTime;

    public ReactivePropertySlim<MediaTime> MaximumTime { get; }

    IReadOnlyReactiveProperty<MediaTime> IEditorClock.MaximumTime => MaximumTime;

    public void Seek(MediaTime time)
    {
        if (Dispatcher.UIThread.CheckAccess())
            _currentTime.Value = time;
        else
            Dispatcher.UIThread.Post(() => _currentTime.Value = time);
    }

    private void Subscribe(Element element)
    {
        element.Start.ValueChanged += OnElementStartChanged;
        element.Length.ValueChanged += OnElementLengthChanged;
    }

    private void Unsubscribe(Element element)
    {
        element.Start.ValueChanged -= OnElementStartChanged;
        element.Length.ValueChanged -= OnElementLengthChanged;
    }

    private void OnElementAttached(Element obj)
    {
        Subscribe(obj);
        AddEnd(obj.Range.End);
        CalculateMaximumTime();
    }

    private void OnElementDetached(Element obj)
    {
        Unsubscribe(obj);
        RemoveEnd(obj.Range.End);
        CalculateMaximumTime();
    }

    private void OnElementStartChanged(object? sender, PropertyValueChangedEventArgs<MediaTime> e)
    {
        if (e.Property.GetOwnerObject() is Element element)
            OnElementRangeChanged(element, element.Range.WithStart(e.OldValue));
    }

    private void OnElementLengthChanged(object? sender, PropertyValueChangedEventArgs<MediaTime> e)
    {
        if (e.Property.GetOwnerObject() is Element element)
            OnElementRangeChanged(element, element.Range.WithDuration(e.OldValue));
    }

    private void OnElementRangeChanged(Element element, TimeRange oldRange)
    {
        RemoveEnd(oldRange.End);
        AddEnd(element.Range.End);
        CalculateMaximumTime();
    }

    private void CalculateMaximumTime()
    {
        MaximumTime.Value = _ends.Count > 0 ? _ends.Max : MediaTime.Zero;
    }

    private void AddEnd(MediaTime end)
    {
        if (_endCounts.TryGetValue(end, out int count)) _endCounts[end] = count + 1;
        else
        {
            _endCounts.Add(end, 1);
            _ends.Add(end);
        }
    }

    private void RemoveEnd(MediaTime end)
    {
        if (_endCounts[end] > 1) _endCounts[end]--;
        else
        {
            _endCounts.Remove(end);
            _ends.Remove(end);
        }
    }

    public void Dispose()
    {
        _scene.Children.Attached -= OnElementAttached;
        _scene.Children.Detached -= OnElementDetached;

        foreach (Element element in _scene.Children)
        {
            Unsubscribe(element);
        }

        _currentTime.Dispose();
        MaximumTime.Dispose();
        _endCounts.Clear();
        _ends.Clear();
    }
}
