using System.Collections.Immutable;

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

        End = EndTime.CombineLatest(editViewModel.Scale)
            .Select(v => v.First.ToPixel(v.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        editViewModel.FrameCacheManager.BlocksUpdated += OnFrameCacheManagerBlocksUpdated;
        _disposables.Add(Disposable.Create(editViewModel.FrameCacheManager, m => m.BlocksUpdated -= OnFrameCacheManagerBlocksUpdated));
    }

    private void OnFrameCacheManagerBlocksUpdated(ImmutableArray<(int Start, int Length)> obj)
    {
        CacheBlocks.Value = obj.SelectArray(
            v => new CacheBlock(_editViewModel.Player.GetFrameRate(), v.Start, v.Length));
    }

    public ReactivePropertySlim<TimeSpan> StartTime { get; } = new();

    public ReactivePropertySlim<TimeSpan> EndTime { get; } = new();

    public ReadOnlyReactivePropertySlim<double> Start { get; }

    public ReadOnlyReactivePropertySlim<double> End { get; }

    public ReactivePropertySlim<CacheBlock[]> CacheBlocks { get; } = new([]);

    public sealed class CacheBlock
    {
        public CacheBlock(int rate, int start, int length)
        {
            Start = TimeSpanExtensions.ToTimeSpan(start, rate);
            Length = TimeSpanExtensions.ToTimeSpan(length, rate);
        }

        public TimeSpan Start { get; }

        public TimeSpan Length { get; }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        CacheBlocks.Value = [];
    }
}
