using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Threading;

using SkiaSharp;

namespace Beutl.Rendering;

public class DeferredRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly SortedDictionary<int, ILayerContext> _objects = new();
    private readonly List<Rect> _clips = new();
    private readonly Canvas _graphics;
    private readonly Rect _canvasBounds;
    private readonly FpsText _fpsText = new();
    private readonly InstanceClock _instanceClock = new();
    private TimeSpan _lastTimeSpan;

    public DeferredRenderer(int width, int height)
    {
        _graphics = s_dispatcher.Invoke(() => new Canvas(width, height));
        _canvasBounds = new Rect(_graphics.Size.ToSize(1));
    }

    ~DeferredRenderer()
    {
        Dispose();
    }

    public ICanvas Graphics => _graphics;

    public Dispatcher Dispatcher => s_dispatcher;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public bool DrawFps
    {
        get => _fpsText.DrawFps;
        set => _fpsText.DrawFps = value;
    }

    public IClock Clock => _instanceClock;

    public ILayerContext? this[int index]
    {
        get => _objects.TryGetValue(index, out ILayerContext? value) ? value : null;
        set
        {
            if (value != null)
            {
                _objects[index] = value;
            }
            else
            {
                _objects.Remove(index);
            }
        }
    }

    public event EventHandler<IRenderer.RenderResult>? RenderInvalidated;

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public IRenderer.RenderResult Render(TimeSpan timeSpan)
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            IsRendering = true;
            _instanceClock.CurrentTime = timeSpan;
            using (_fpsText.StartRender(this))
            {
                RenderCore(timeSpan);
            }

            IsRendering = false;
        }

        _lastTimeSpan = timeSpan;
        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    protected virtual void RenderCore(TimeSpan timeSpan)
    {
        var objects = new KeyValuePair<int, ILayerContext>[_objects.Count];
        _objects.CopyTo(objects, 0);
        Func(objects, 0, objects.Length, timeSpan);

        using (Graphics.PushCanvas())
        {
            using var path = new SKPath();
            for (int i = 0; i < _clips.Count; i++)
            {
                Rect item = _clips[i];
                path.AddRect(SKRect.Create(item.X, item.Y, item.Width, item.Height));
            }

            Graphics.ClipPath(path);

            if (_clips.Count > 0)
            {
                Graphics.Clear();
            }

            foreach (KeyValuePair<int, ILayerContext> item in objects)
            {
                IRenderable? renderable = item.Value[timeSpan]?.Value;
                if (renderable?.IsDirty ?? false)
                {
                    renderable.Render(this);
                }
            }

            _clips.Clear();
        }
    }

    // _clipsにrect1, rect2を追加する
    private void AddDirtyRects(Rect rect1, Rect rect2)
    {
        if (!rect1.IsEmpty)
        {
            if (!_canvasBounds.Contains(rect1))
            {
                rect1 = ClipToCanvasBounds(rect1);
            }
            _clips.Add(rect1);
        }
        if (!rect2.IsEmpty)
        {
            if (!_canvasBounds.Contains(rect2))
            {
                rect2 = ClipToCanvasBounds(rect2);
            }

            if (rect1 != rect2)
            {
                _clips.Add(rect2);
            }
        }
    }

    // 変更されているオブジェクトのBoundsを_clipsに追加して、
    // そのオブジェクトが影響を与えるオブジェクトも同様の処理をする
    private void Func(ReadOnlySpan<KeyValuePair<int, ILayerContext>> items, int start, int length, TimeSpan timeSpan)
    {
        for (int i = length - 1; i >= start; i--)
        {
            KeyValuePair<int, ILayerContext> item = items[i];
            ILayerContext context = item.Value;
            LayerNode? layerNode = context[timeSpan];
            LayerNode? lastLayerNode = context[_lastTimeSpan];
            IRenderable? renderable = layerNode?.Value;
            IRenderable? lastRenderable = lastLayerNode?.Value;

            if (layerNode != lastLayerNode)
            {
                if (lastRenderable is Drawable lastDrawable)
                {
                    AddDirtyRect(lastDrawable.Bounds);
                    lastDrawable.Invalidate();
                }
            }

            if (renderable is Drawable drawable)
            {
                Rect rect1 = drawable.Bounds;
                drawable.Measure(_canvasBounds.Size);
                Rect rect2 = drawable.Bounds;

                if (drawable.IsDirty)
                {
                    AddDirtyRects(rect1, rect2);
                    drawable.Invalidate();

                    //Func(items, 0, i);
                    Func(items, i + 1, items.Length, timeSpan);
                }
                else if (renderable.IsVisible && HitTestClips(rect1, rect2))
                {
                    drawable.Invalidate();
                }
            }
        }
    }

    // rectがcanvasのBoundsに丸める
    private Rect ClipToCanvasBounds(Rect rect)
    {
        return new Rect(
            new Point(Math.Max(rect.Left, 0), Math.Max(rect.Top, 0)),
            new Point(Math.Min(rect.Right, _canvasBounds.Width), Math.Min(rect.Bottom, _canvasBounds.Height)));
    }

    // _clipsがrect1またはrect2と交差する場合trueを返す。
    private bool HitTestClips(Rect rect1, Rect rect2)
    {
        for (int i = 0; i < _clips.Count; i++)
        {
            Rect item = _clips[i];
            if (!item.IsEmpty &&
                (item.Intersects(rect1) || item.Intersects(rect2)))
            {
                return true;
            }
        }

        return false;
    }

    public async void Invalidate(TimeSpan timeSpan)
    {
        if (RenderInvalidated != null)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render(timeSpan));
            RenderInvalidated.Invoke(this, result);
            result.Bitmap.Dispose();
        }
    }

    private void AddDirtyRect(Rect rect)
    {
        if (!rect.IsEmpty)
        {
            if (!_canvasBounds.Contains(rect))
            {
                rect = ClipToCanvasBounds(rect);
            }
            _clips.Add(rect);
        }
    }

    void IRenderer.AddDirtyRect(Rect rect)
    {
        AddDirtyRect(rect);
    }
}
