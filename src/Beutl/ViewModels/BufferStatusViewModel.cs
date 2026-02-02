using System.Collections.Immutable;

using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Models;
using Beutl.Models;

using Reactive.Bindings;

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

    public void Dispose()
    {
        _disposables.Dispose();
        CacheBlocks.Value = [];
    }
}
