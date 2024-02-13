using System.Collections.Immutable;

using Avalonia.Threading;

using Beutl.Models;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class BufferStatusViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private CancellationTokenSource? _cts;

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

        editViewModel.FrameCacheManager
            .Select(v => Observable.FromEvent<ImmutableArray<FrameCacheManager.CacheBlock>>(h => v.BlocksUpdated += h, h => v.BlocksUpdated -= h))
            .Switch()
            .Subscribe(OnFrameCacheManagerBlocksUpdated)
            .DisposeWith(_disposables);
    }

    private void OnFrameCacheManagerBlocksUpdated(ImmutableArray<FrameCacheManager.CacheBlock> obj)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Dispatcher.UIThread.InvokeAsync(
            () => CacheBlocks.Value = obj.Select(v => new CacheBlock(_editViewModel.Player.GetFrameRate(), v.Start, v.Length, v.IsLocked)).ToArray(),
            DispatcherPriority.Background,
            _cts.Token);
    }

    public ReactivePropertySlim<TimeSpan> StartTime { get; } = new();

    public ReactivePropertySlim<TimeSpan> EndTime { get; } = new();

    public ReadOnlyReactivePropertySlim<double> Start { get; }

    public ReadOnlyReactivePropertySlim<double> End { get; }

    public ReactivePropertySlim<CacheBlock[]> CacheBlocks { get; } = new([]);

    public sealed class CacheBlock(int rate, int start, int length, bool isLocked)
    {
        public TimeSpan Start { get; } = TimeSpanExtensions.ToTimeSpan(start, rate);

        public TimeSpan Length { get; } = TimeSpanExtensions.ToTimeSpan(length, rate);

        public int StartFrame { get; } = start;

        public int LengthFrame { get; } = length;

        public bool IsLocked { get; } = isLocked;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        CacheBlocks.Value = [];
    }
}
