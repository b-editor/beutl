using System.Collections.Immutable;

using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Models;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class BufferStatusViewModel : IBufferStatus, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private CancellationTokenSource? _cts;

    public BufferStatusViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        Start = StartTime.CombineLatest(editViewModel.Scale)
            .Select(v => v.First.TimeToPixel(v.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        End = EndTime.CombineLatest(editViewModel.Scale)
            .Select(v => v.First.TimeToPixel(v.Second))
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

    IReadOnlyReactiveProperty<TimeSpan> IBufferStatus.StartTime => StartTime;

    IReadOnlyReactiveProperty<TimeSpan> IBufferStatus.EndTime => EndTime;

    public ReadOnlyReactivePropertySlim<double> Start { get; }

    IReadOnlyReactiveProperty<double> IBufferStatus.Start => Start;

    public ReadOnlyReactivePropertySlim<double> End { get; }

    IReadOnlyReactiveProperty<double> IBufferStatus.End => End;

    public ReactivePropertySlim<CacheBlock[]> CacheBlocks { get; } = new([]);

    IReadOnlyReactiveProperty<CacheBlock[]> IBufferStatus.CacheBlocks => CacheBlocks;

    public void UpdateBlocks()
    {
        _editViewModel.FrameCacheManager.Value?.UpdateBlocks();
    }

    public void ClearCache()
    {
        _editViewModel.FrameCacheManager.Value?.Clear();
    }

    public void LockCache(int startFrame, int endFrame)
    {
        _editViewModel.FrameCacheManager.Value?.Lock(startFrame, endFrame);
    }

    public void UnlockCache(int startFrame, int endFrame)
    {
        _editViewModel.FrameCacheManager.Value?.Unlock(startFrame, endFrame);
    }

    public void DeleteCache(int startFrame, int endFrame)
    {
        _editViewModel.FrameCacheManager.Value?.DeleteAndUpdateBlocks([(startFrame, endFrame)]);
    }

    public long CalculateCacheByteCount(int startFrame, int endFrame)
    {
        return _editViewModel.FrameCacheManager.Value?.CalculateByteCount(startFrame, endFrame) ?? 0;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        CacheBlocks.Value = [];
    }
}
