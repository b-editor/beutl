using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;
using Beutl.Threading;

using SkiaSharp;

namespace Beutl.Rendering.Cache;

public sealed class RenderCache(IGraphicNode node) : IDisposable
{
    private readonly WeakReference<IGraphicNode> _node = new(node);
    private readonly List<(Ref<SKSurface>, Rect)> _cache = new(1);

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

    public bool IsCached => _cache.Count != 0;

    public int CacheCount => _cache.Count;

    public DateTime LastAccessedTime { get; private set; }

    public bool IsDisposed { get; private set; }

    public List<WeakReference<IGraphicNode>>? Children { get; private set; }

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

    public void CaptureChildren()
    {
        if (_node.TryGetTarget(out IGraphicNode? node)
            && node is ContainerNode container)
        {
            if (Children == null)
            {
                Children = new List<WeakReference<IGraphicNode>>(container.Children.Count);
            }
            else
            {
                Children.EnsureCapacity(container.Children.Count);
            }

            CollectionsMarshal.SetCount(Children, container.Children.Count);
            Span<WeakReference<IGraphicNode>> span = CollectionsMarshal.AsSpan(Children);

            for (int i = 0; i < container.Children.Count; i++)
            {
                IGraphicNode item = container.Children[i];
                ref WeakReference<IGraphicNode> refrence = ref span[i];

                if (refrence == null)
                {
                    refrence = new WeakReference<IGraphicNode>(item);
                }
                else
                {
                    refrence.SetTarget(item);
                }
            }
        }
    }

    public bool SameChildren()
    {
        if (Children != null
            && _node.TryGetTarget(out IGraphicNode? node)
            && node is ContainerNode container)
        {
            if (Children.Count != container.Children.Count)
                return false;

            for (int i = 0; i < Children.Count; i++)
            {
                WeakReference<IGraphicNode> capturedRef = Children[i];
                IGraphicNode current = container.Children[i];
                if (!capturedRef.TryGetTarget(out IGraphicNode? captured)
                    || !ReferenceEquals(captured, current))
                {
                    return false;
                }
            }

            return true;
        }
        else
        {
            return true;
        }
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
        if (_cache.Count != 0)
        {
            Debug.WriteLine($"[RenderCache:Invalildated] '{(_node.TryGetTarget(out IGraphicNode? node) ? node : null)}'");
        }
#endif

        foreach ((Ref<SKSurface>, Rect) item in _cache)
        {
            item.Item1.Dispose();
        }
        _cache.Clear();
        _cachedAt = -1;
    }

    public void Dispose()
    {
        void DisposeOnRenderThread()
        {
            if (_cache.Count != 0)
            {
                (Ref<SKSurface>, Rect)[] tmp = [.. _cache];
                _cache.Clear();

                RenderThread.Dispatcher.Dispatch(() =>
                {
                    foreach ((Ref<SKSurface>, Rect) item in tmp)
                    {
                        item.Item1.Dispose();
                    }
                }, DispatchPriority.Low);
            }

            IsDisposed = true;
        }

        if (!IsDisposed)
        {
            if (RenderThread.Dispatcher.CheckAccess())
            {
                foreach ((Ref<SKSurface>, Rect) item in _cache)
                {
                    item.Item1.Dispose();
                }
                _cache.Clear();
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
        if (_cache.Count == 0)
        {
            throw new Exception("キャッシュはありません");
        }

        (Ref<SKSurface>, Rect) c = _cache[0];
        bounds = c.Item2;
        LastAccessedTime = DateTime.UtcNow;
        return c.Item1.Clone();
    }

    public void StoreCache(Ref<SKSurface> surface, Rect bounds)
    {
        Invalidate();

        _cache.Add((surface.Clone(), bounds));

        if (_accessor != null)
        {
            const int Count = FixedArray.Count;
            _cachedAt = _accessor.Get((_accessor.Index + (Count - 1)) % Count);
        }
        else
        {
            _cachedAt = 0;
        }

        LastAccessedTime = DateTime.UtcNow;
    }

    public (Ref<SKSurface> Surface, Rect Bounds)[] UseCache()
    {
        LastAccessedTime = DateTime.UtcNow;
        return _cache.Select(i => (i.Item1.Clone(), i.Item2)).ToArray();
    }

    public void StoreCache(ReadOnlySpan<(Ref<SKSurface> Surface, Rect Bounds)> items)
    {
        Invalidate();

        foreach ((Ref<SKSurface> surface, Rect bounds) in items)
        {
            _cache.Add((surface.Clone(), bounds));
        }

        if (_accessor != null)
        {
            const int Count = FixedArray.Count;
            _cachedAt = _accessor.Get((_accessor.Index + (Count - 1)) % Count);
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
