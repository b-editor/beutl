using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class BufferStatusViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;

    public BufferStatusViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        Start = StartTime.CombineLatest(editViewModel.Scale)
            .Select(v => v.First.ToPixel(v.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<TimeSpan> length = EndTime.CombineLatest(StartTime)
            .Select(v => v.First - v.Second);

        Width = length.CombineLatest(editViewModel.Scale)
            .Select(v => Math.Max(0, v.First.ToPixel(v.Second)))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public ReactivePropertySlim<TimeSpan> StartTime { get; } = new();

    public ReactivePropertySlim<TimeSpan> EndTime { get; } = new();

    public ReadOnlyReactivePropertySlim<double> Start { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
