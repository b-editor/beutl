using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IBufferStatus
{
    IReadOnlyReactiveProperty<TimeSpan> StartTime { get; }

    IReadOnlyReactiveProperty<TimeSpan> EndTime { get; }

    IReadOnlyReactiveProperty<double> Start { get; }  // pixel

    IReadOnlyReactiveProperty<double> End { get; }    // pixel

    IReadOnlyReactiveProperty<CacheBlock[]> CacheBlocks { get; }

    void UpdateBlocks();

    // キャッシュ操作
    void ClearCache();

    void LockCache(int startFrame, int endFrame);

    void UnlockCache(int startFrame, int endFrame);

    void DeleteCache(int startFrame, int endFrame);

    long CalculateCacheByteCount(int startFrame, int endFrame);
}
