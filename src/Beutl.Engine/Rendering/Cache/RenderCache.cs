using System.Diagnostics;
using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;
using Beutl.Threading;

using SkiaSharp;

namespace Beutl.Rendering.Cache;

public sealed class RenderCache(IGraphicNode node) : IDisposable
{
    private readonly WeakReference<IGraphicNode> _node = new(node);
    private Ref<SKSurface>? _cache;
    private Rect _cacheBounds;

    private int _count;

    // キャッシュしたときの進捗の値
    private int _cachedAt = -1;
    // 前回のフレームと比べたときに同じだった操作の数（進捗）
    private FixedArrayAccessor? _accessor;
    private int _denum;

    ~RenderCache()
    {
        if (!IsDisposed)
            Dispose();
    }

    private FixedArrayAccessor Accessor => _accessor ??= new();

    public bool IsCached => _cache != null;

    public DateTime LastAccessedTime { get; private set; }

    public bool IsDisposed { get; private set; }

    public void ReportRenderCount(int count)
    {
        _count = count;
    }

    public void IncrementRenderCount()
    {
        _count++;
    }

    // 一つのノードで処理が別れている場合、どこまで同じかを報告する
    public void ReportSameNumber(int value, int count)
    {
        _denum = count;

        Accessor.Set(value);
        Accessor.IncrementIndex();

        // キャッシュしたときのpがvalueより大きい場合、キャッシュを無効化
        // 例えば、キャッシュ時には三つのエフェクトが含まれている状態だったが、<- (1)
        // 最後の一つだけ変わったなど。
        if (_cachedAt > value)
        {
            Invalidate();
        }
        // `GetMinNumber()` と `_cachedAt`がかけ離れている、
        // 例えば、上の (1) の状況で、三フレーム以上、変わらないエフェクトが追加されたとき
        else if (GetMinNumber() > _cachedAt)
        {
            Invalidate();
        }
    }

    public int GetMinNumber()
    {
        return _accessor?.Minimum() ?? 0;
    }

    public bool CanCache()
    {
        if (_count >= FixedArray.Count)
        {
            return true;
        }
        else if (_accessor != null)
        {
            for (int i = 0; i < FixedArray.Count; i++)
            {
                if (_accessor.Get(i) != _denum)
                {
                    return false;
                }
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    public bool CanCacheBoundary()
    {
        return GetMinNumber() >= 1 || _count >= FixedArray.Count;
    }

    public void Invalidate()
    {
        RenderThread.Dispatcher.CheckAccess();
#if DEBUG
        if (_cache != null)
        {
            Debug.WriteLine($"[RenderCache:Invalildated] '{(_node.TryGetTarget(out IGraphicNode? node) ? node : null)}'");
        }
#endif

        _cache?.Dispose();
        _cache = null;
        _cacheBounds = Rect.Empty;
        _cachedAt = -1;
    }

    public void Dispose()
    {
        void DisposeOnRenderThread()
        {
            if (_cache != null)
            {
                Ref<SKSurface> tmp = _cache;
                _cache = null;
                _cacheBounds = Rect.Empty;

                RenderThread.Dispatcher.Dispatch(tmp.Dispose, DispatchPriority.Low);
            }

            IsDisposed = true;
        }

        if (!IsDisposed)
        {
            if (RenderThread.Dispatcher.CheckAccess())
            {
                _cache?.Dispose();
                _cache = null;
                _cacheBounds = Rect.Empty;
                IsDisposed = true;
            }
            else
            {
                DisposeOnRenderThread();
            }

            GC.SuppressFinalize(this);
        }
    }

    public Ref<SKSurface> UseCache(out Rect bounds)
    {
        if (_cache == null)
        {
            throw new Exception("キャッシュはありません");
        }

        bounds = _cacheBounds;
        LastAccessedTime = DateTime.UtcNow;
        return _cache.Clone();
    }

    public void StoreCache(Ref<SKSurface> surface, Rect bounds)
    {
        Invalidate();

        _cache = surface.Clone();
        _cacheBounds = bounds;

        if (_accessor != null)
        {
            const int Count = FixedArray.Count;
            _cachedAt = (_accessor.Index + (Count - 1)) % Count;
        }
        else
        {
            _cachedAt = 0;
        }

        LastAccessedTime = DateTime.UtcNow;
    }

    private unsafe class FixedArrayAccessor
    {
        public FixedArray Array;
        public int Index;

        public void IncrementIndex()
        {
            Index++;
            // 折り返す
            Index %= FixedArray.Count;
        }

        public ref int Get(int index)
        {
            if (index is < 0 or >= FixedArray.Count)
                throw new Exception("0 <= index <= 2");

            return ref Array.Array[index];
        }

        public void Set(int value)
        {
            Array.Array[Index] = value;
        }

        public int Minimum()
        {
            int value = int.MaxValue;
            for (int i = 0; i < FixedArray.Count; i++)
            {
                value = Math.Min(Array.Array[i], value);
            }

            return value;
        }
    }

    private unsafe struct FixedArray
    {
        public const int Count = 3;
        public fixed int Array[Count];

        public Span<int> Span => new(Unsafe.AsPointer(ref Array[0]), Count);
    }
}
