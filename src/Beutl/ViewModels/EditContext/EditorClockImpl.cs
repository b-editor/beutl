using Avalonia.Threading;
using Beutl.Media;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class EditorClockImpl : IEditorClock, IDisposable
{
    private readonly Scene _scene;
    private readonly ReactivePropertySlim<TimeSpan> _currentTime = new();

    public EditorClockImpl(Scene scene)
    {
        _scene = scene;
        MaximumTime = new ReactivePropertySlim<TimeSpan>();

        foreach (Element element in _scene.Children)
        {
            Subscribe(element);
        }

        _scene.Children.Attached += OnElementAttached;
        _scene.Children.Detached += OnElementDetached;

        CalculateMaximumTime();
    }

    public IReadOnlyReactiveProperty<TimeSpan> CurrentTime => _currentTime;

    public ReactivePropertySlim<TimeSpan> MaximumTime { get; }

    IReadOnlyReactiveProperty<TimeSpan> IEditorClock.MaximumTime => MaximumTime;

    public void Seek(TimeSpan time)
    {
        if (Dispatcher.UIThread.CheckAccess())
            _currentTime.Value = time;
        else
            Dispatcher.UIThread.Post(() => _currentTime.Value = time);
    }

    private void Subscribe(Element element)
    {
        element.StartProperty.ValueChanged += OnElementStartChanged;
        element.LengthProperty.ValueChanged += OnElementLengthChanged;
    }

    private void Unsubscribe(Element element)
    {
        element.StartProperty.ValueChanged -= OnElementStartChanged;
        element.LengthProperty.ValueChanged -= OnElementLengthChanged;
    }

    private void OnElementAttached(Element obj)
    {
        Subscribe(obj);

        if (MaximumTime.Value < obj.Range.End)
        {
            MaximumTime.Value = obj.Range.End;
        }
    }

    private void OnElementDetached(Element obj)
    {
        Unsubscribe(obj);

        // A bulk removal raises Detached after removing every element, so a removed element never raises the
        // maximum; it is recalculated over the elements left when the removed one ended at it.
        if (MaximumTime.Value == obj.Range.End)
        {
            CalculateMaximumTime();
        }
    }

    private void OnElementStartChanged(object? sender, PropertyValueChangedEventArgs<TimeSpan> e)
    {
        if (e.Property.GetOwnerObject() is Element element)
            OnElementRangeChanged(element, element.Range.WithStart(e.OldValue));
    }

    private void OnElementLengthChanged(object? sender, PropertyValueChangedEventArgs<TimeSpan> e)
    {
        if (e.Property.GetOwnerObject() is Element element)
            OnElementRangeChanged(element, element.Range.WithDuration(e.OldValue));
    }

    private void OnElementRangeChanged(Element element, TimeRange oldRange)
    {
        if (MaximumTime.Value < element.Range.End)
        {
            MaximumTime.Value = element.Range.End;
        }
        else if (MaximumTime.Value == oldRange.End)
        {
            CalculateMaximumTime();
        }
    }

    private void CalculateMaximumTime()
    {
        MaximumTime.Value = _scene.Children.Count > 0
            ? _scene.Children.Max(i => i.Range.End)
            : TimeSpan.Zero;
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
    }
}
