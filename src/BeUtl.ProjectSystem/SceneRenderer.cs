using BeUtl.Collections;
using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Threading;

using SkiaSharp;

namespace BeUtl;

internal class SceneRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly Scene _scene;
    private readonly SortedDictionary<int, ILayerScope> _objects = new();
    private readonly List<Rect> _clips = new();
    private readonly Canvas _graphics;
    private readonly Rect _canvasBounds;
    private readonly List<Layer> _begin = new();
    private readonly List<Layer> _end = new();
    private readonly List<Layer> _layers = new();
    private TimeSpan _recentTime = TimeSpan.MinValue;

    public SceneRenderer(Scene scene, int width, int height)
    {
        _scene = scene;
        _graphics = s_dispatcher.Invoke(() => new Canvas(width, height));
        _canvasBounds = new Rect(_graphics.Size.ToSize(1));
    }

    public ICanvas Graphics => _graphics;

    public Dispatcher Dispatcher => s_dispatcher;

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public ILayerScope? this[int index]
    {
        get => _objects.ContainsKey(index) ? _objects[index] : null;
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

    public void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();

        IsDisposed = true;
    }

    public IRenderer.RenderResult Render()
    {
        if (_recentTime == TimeSpan.MinValue)
        {
            _recentTime = FrameNumber;
        }

        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            DevideLayers();

            for (int i = 0; i < _layers.Count; i++)
            {
                Layer layer = _layers[i];
                var args = new OperationRenderArgs(FrameNumber, this, layer.Scope);
                foreach (LayerOperation item in layer.Operations)
                {
                    item.ApplySetters(args);
                }
            }

            // IScopedRenderableをLayerに保持させる
            for (int i = 0; i < _end.Count; i++)
            {
                Layer item = _end[i];
                foreach (LayerOperation item2 in item.Operations)
                {
                    item2.EndingRender(item.Scope);
                }

                _clips.AddRange(item.Scope.OfType<Drawable>().Select(item => ClipToCanvasBounds(item.Bounds)));
            }

            for (int i = 0; i < _begin.Count; i++)
            {
                Layer item = _begin[i];
                foreach (LayerOperation item2 in item.Operations)
                {
                    item2.BeginningRender(item.Scope);
                }
            }

            var reversed = new KeyValuePair<int, ILayerScope>[_objects.Count];
            _objects.CopyTo(reversed, 0);
            Array.Reverse(reversed);
            Func(reversed, 0, reversed.Length);

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

                for (int i = reversed.Length - 1; i >= 0; i--)
                {
                    KeyValuePair<int, ILayerScope> item = reversed[i];
                    for (int ii = item.Value.Count - 1; ii >= 0; ii--)
                    {
                        IRenderable item2 = item.Value[ii];
                        if (!item2.IsVisible)
                        {
                            item.Value.RemoveAt(ii);
                        }
                        else if (item2 is Drawable drawable && drawable.IsDirty)
                        {
                            item2.Render(this);
                        }
                    }
                }

                _clips.Clear();
            }
        }

        _recentTime = FrameNumber;
        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    // 変更されているオブジェクトのBoundsを_clipsに追加して、
    // そのオブジェクトが影響を与えるオブジェクトも同様の処理をする
    private void Func(ReadOnlySpan<KeyValuePair<int, ILayerScope>> items, int start, int length)
    {
        for (int i = start; i < length; i++)
        {
            KeyValuePair<int, ILayerScope> item = items[i];
            for (int ii = 0; ii < item.Value.Count; ii++)
            {
                IRenderable item2 = item.Value[ii];
                if (item2 is Drawable drawable)
                {
                    Rect rect1 = drawable.Bounds;
                    drawable.Measure(_canvasBounds.Size);
                    Rect rect2 = drawable.Bounds;

                    if (item2.IsVisible)
                    {
                        if (drawable.IsDirty)
                        {
                            AddClips(rect1, rect2);
                            drawable.InvalidateVisual();

                            Func(items, 0, i);
                        }
                        else if (ContainsClips(rect1, rect2))
                        {
                            drawable.InvalidateVisual();
                        }
                        else if (HitTestClips(rect1, rect2))
                        {
                            AddClips(rect1, rect2);
                            drawable.InvalidateVisual();
                        }
                    }
                }
            }
        }
    }

    // _clipsにrect1, rect2を追加する
    private void AddClips(Rect rect1, Rect rect2)
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

    // rect1またはrect2に_clipsのどれかが含まれている場合trueを返す。
    private bool ContainsClips(Rect rect1, Rect rect2)
    {
        for (int i = 0; i < _clips.Count; i++)
        {
            Rect item = _clips[i];
            if (!item.IsEmpty &&
                (rect1.Contains(item) || rect2.Contains(item)))
            {
                return true;
            }
        }

        return false;
    }

    // Layersを振り分ける
    private void DevideLayers()
    {
        _begin.Clear();
        _end.Clear();
        _layers.Clear();
        foreach (Layer? item in _scene.Layers)
        {
            bool recent = InRange(item, _recentTime);
            bool current = InRange(item, _scene.CurrentFrame);

            if (current)
            {
                _layers.Add(item);
            }

            if (!recent && current)
            {
                // _recentTimeの範囲外でcurrntTimeの範囲内
                _begin.Add(item);
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                _end.Add(item);
            }
        }
    }

    // itemがtsの範囲内かを確かめます
    private static bool InRange(Layer item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
    }

    public async void Invalidate()
    {
        if (RenderInvalidated != null)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render());
            RenderInvalidated.Invoke(this, result);
            result.Bitmap.Dispose();
        }
    }

    //private static int ToFrameNumber(TimeSpan tp, int rate)
    //{
    //    return (int)(tp.TotalSeconds * rate);
    //}

    //private static int TicksPerFrame(int rate)
    //{
    //    return 10000000 / rate;
    //}

    //private static TimeSpan ToTimeSpan(int f, int rate)
    //{
    //    return TimeSpan.FromSeconds(f / (double)rate);
    //}
}
