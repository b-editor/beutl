using System.Collections.Generic;

using BeUtl.Collections;
using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Threading;

using SkiaSharp;

namespace BeUtl;

public class ScopedRenderable : List<IScopedRenderable.LayerItem>, IScopedRenderable
{

}

internal class SceneRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly Scene _scene;
    private readonly RenderableList _renderables = new();
    private List<Layer>? _cache;
    private SortedDictionary<int, IScopedRenderable> _objects = new();
    private readonly List<Rect> _clips = new();
    private readonly Canvas _graphics;
    private TimeSpan _recentTime = TimeSpan.MinValue;

    public SceneRenderer(Scene scene, int width, int height)
    {
        _scene = scene;
        _graphics = s_dispatcher.Invoke(() => new Canvas(width, height));
    }

    public ICanvas Graphics => _graphics;

    public Dispatcher Dispatcher => s_dispatcher;

    public TimeSpan FrameNumber => _scene.CurrentFrame;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public event EventHandler<IRenderer.RenderResult>? RenderInvalidated;

    public void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();
        _cache = null;

        IsDisposed = true;
    }

    public IRenderer.RenderResult Render()
    {
        if (_recentTime == TimeSpan.MinValue)
        {
            _recentTime = FrameNumber;
            return ImmediateRender();
        }

        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            var (begin, end) = GetBeginEndLayer();
            var layers = GetLayers();

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                foreach (var item in layer.Operations)
                {
                    item.ApplySetters(new(FrameNumber, this, _renderables));
                }
            }

            // IScopedRenderableをLayerに保持させる
            foreach (var item in end)
            {
                IScopedRenderable? scope = null;
                if (_objects.ContainsKey(item.ZIndex))
                {
                    scope = _objects[item.ZIndex];
                }
                else
                {
                    _objects[item.ZIndex] = scope = new ScopedRenderable();
                }

                foreach (var item2 in item.Operations)
                {
                    item2.EndingRender(scope);
                }

                _clips.AddRange(scope.Select(i => i.Item).OfType<Drawable>().Select(i => i._prevBounds));
            }

            foreach (var item in begin)
            {
                IScopedRenderable? scope = null;
                if (_objects.ContainsKey(item.ZIndex))
                {
                    scope = _objects[item.ZIndex];
                }
                else
                {
                    _objects[item.ZIndex] = scope = new ScopedRenderable();
                }

                foreach (var item2 in item.Operations)
                {
                    item2.BeginningRender(scope);
                }
            }

            void Func(ReadOnlySpan<KeyValuePair<int, IScopedRenderable>> items, int start, int length)
            {
                for (int i = start; i < length; i++)
                {
                    var item = items[i];
                    foreach (var item2 in item.Value)
                    {
                        if (item2.Item is Drawable drawable)
                        {
                            var rect1 = drawable._prevBounds;
                            var rect2 = drawable.Measure(Graphics.Size);

                            if (!item2.IsInvalidated)
                            {
                                if (drawable.IsDirty)
                                {
                                    if (!rect1.IsEmpty)
                                    {
                                        _clips.Add(rect1);
                                    }
                                    if (!rect2.IsEmpty)
                                    {
                                        _clips.Add(rect2);
                                    }
                                    drawable.InvalidateVisual();

                                    Func(items, 0, i);
                                }
                                else if (ContainsClips(rect1, rect2))
                                {
                                    drawable.InvalidateVisual();
                                }
                                else if (HitTestClips(rect1, rect2))
                                {
                                    if (!rect1.IsEmpty)
                                    {
                                        _clips.Add(rect1);
                                    }
                                    if (!rect2.IsEmpty)
                                    {
                                        _clips.Add(rect2);
                                    }
                                    drawable.InvalidateVisual();
                                }
                            }
                        }
                    }
                }
            }

            var reversed = _objects.ToArray();
            Array.Reverse(reversed);
            Func(reversed, 0, reversed.Length);

            using (Graphics.PushCanvas())
            {
                using var path = new SKPath();
                foreach (var item in _clips)
                {
                    path.AddRect(SKRect.Create(item.X, item.Y, item.Width, item.Height));
                }

                Graphics.ClipPath(path);

                if (_clips.Count > 0)
                {
                    Graphics.Clear();
                }

                foreach (var item in _objects)
                {
                    for (int i = item.Value.Count - 1; i >= 0; i--)
                    {
                        var item2 = item.Value[i];
                        if (item2.IsInvalidated)
                        {
                            item.Value.RemoveAt(i);
                        }
                        else if (item2.Item is Drawable drawable && drawable.IsDirty)
                        {
                            item2.Item.Render(this);
                        }
                    }
                }

                _clips.Clear();
            }
        }

        _recentTime = FrameNumber;
        return new IRenderer.RenderResult(Graphics.GetBitmap());
        //_recentTime
    }

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

    private List<Layer> GetLayers()
    {
        var list = new List<Layer>();
        foreach (Layer? item in _scene.Layers)
        {
            if (InRange(item, _scene.CurrentFrame))
            {
                list.Add(item);
            }
        }
        return list;
    }

    private (List<Layer> Begin, List<Layer> End) GetBeginEndLayer()
    {
        var begin = new List<Layer>();
        var end = new List<Layer>();
        foreach (Layer? item in _scene.Layers)
        {
            bool recent = InRange(item, _recentTime);
            bool current = InRange(item, _scene.CurrentFrame);

            if (!recent && current)
            {
                // _recentTimeの範囲外でcurrntTimeの範囲内
                begin.Add(item);
            }
            else if (recent && !current)
            {
                // _recentTimeの範囲内でcurrntTimeの範囲外
                end.Add(item);
            }
        }
        return (begin, end);
    }

    private IRenderer.RenderResult ImmediateRender()
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            Graphics.Clear();
            TimeSpan ts = FrameNumber;
            List<Layer> layers = FilterAndSortLayers(ts);
            var args = new OperationRenderArgs(ts, this, _renderables);

            for (int i = 0; i < layers.Count; i++)
            {
                Layer item = layers[i];

                if (item.IsEnabled)
                {
                    ProcessLayer(item, args);
                }
            }
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    private void ProcessLayer(Layer layer, in OperationRenderArgs args)
    {
        _renderables.Clear();
        IElementList list = layer.Children;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is LayerOperation op && op.IsEnabled)
            {
                op.ApplySetters(args);
                op.Render(args);
            }
        }

        for (int i = 0; i < _renderables.Count; i++)
        {
            IRenderable renderable = _renderables[i];
            if (!renderable.IsDisposed)
            {
                renderable.Render(this);
                renderable.Dispose();
            }
        }

        _renderables.Clear();
    }

    private List<Layer> FilterAndSortLayers(TimeSpan ts)
    {
        if (_cache == null)
        {
            _cache = new List<Layer>();
        }
        else
        {
            _cache.Clear();
        }
        int length = _scene.Children.Count;
        IElementList children = _scene.Children;

        for (int i = 0; i < length; i++)
        {
            if (children[i] is Layer item &&
                item.Start <= ts &&
                ts < item.Length + item.Start &&
                item.ZIndex >= 0)
            {
                _cache.Add(item);
            }
        }

        _cache.Sort((x, y) => x.ZIndex - y.ZIndex);

        return _cache;
    }

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
