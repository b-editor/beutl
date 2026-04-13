using System.ComponentModel;
using Beutl.Media;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.ViewModels;

internal sealed class EditorClockImpl : IEditorClock, IDisposable
{
    private readonly Scene _scene;

    public EditorClockImpl(Scene scene)
    {
        _scene = scene;
        CurrentTime = new ReactivePropertySlim<TimeSpan>();
        MaximumTime = new ReactivePropertySlim<TimeSpan>();

        foreach (Element element in _scene.Children)
        {
            element.PropertyChanged += OnElementPropertyChanged;
        }

        _scene.Children.Attached += OnElementAttached;
        _scene.Children.Detached += OnElementDetached;

        CalculateMaximumTime();
    }

    public ReactivePropertySlim<TimeSpan> CurrentTime { get; }

    public ReactivePropertySlim<TimeSpan> MaximumTime { get; }

    IReactiveProperty<TimeSpan> IEditorClock.CurrentTime => CurrentTime;

    IReadOnlyReactiveProperty<TimeSpan> IEditorClock.MaximumTime => MaximumTime;

    private void OnElementAttached(Element obj)
    {
        obj.PropertyChanged += OnElementPropertyChanged;

        if (MaximumTime.Value < obj.Range.End)
        {
            MaximumTime.Value = obj.Range.End;
        }
        else
        {
            CalculateMaximumTime();
        }
    }

    private void OnElementDetached(Element obj)
    {
        obj.PropertyChanged -= OnElementPropertyChanged;

        if (MaximumTime.Value < obj.Range.End)
        {
            MaximumTime.Value = obj.Range.End;
        }
        else
        {
            CalculateMaximumTime();
        }
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs<TimeSpan> typedArgs)
        {
            bool startChanged = typedArgs.Property.Id == Element.StartProperty.Id;
            bool lengthChanged = typedArgs.Property.Id == Element.LengthProperty.Id;

            if (sender is Element element && (startChanged || lengthChanged))
            {
                // 変更前の値を取得
                TimeRange oldRange = element.Range;
                if (startChanged) oldRange = oldRange.WithStart(typedArgs.OldValue);
                if (lengthChanged) oldRange = oldRange.WithDuration(typedArgs.OldValue);

                if (MaximumTime.Value < element.Range.End)
                {
                    MaximumTime.Value = element.Range.End;
                }
                else if (MaximumTime.Value == oldRange.End)
                {
                    CalculateMaximumTime();
                }
            }
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
            element.PropertyChanged -= OnElementPropertyChanged;
        }

        CurrentTime.Dispose();
        MaximumTime.Dispose();
    }
}
