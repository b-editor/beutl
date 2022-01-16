using System.Collections.Generic;

using BeUtl.Collections;
using BeUtl.Graphics;
using BeUtl.ProjectSystem;
using BeUtl.Rendering;
using BeUtl.Threading;

using SkiaSharp;

namespace BeUtl;

public class ScopedRenderable : List<IRenderable>, IScopedRenderable
{

}

internal class SceneRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly Scene _scene;
    private List<Layer>? _cache;
    private SortedDictionary<int, IScopedRenderable> _objects = new();
    private readonly List<Rect> _clips = new();
    private readonly Canvas _graphics;
    private readonly Size _canvasSize;
    private readonly Rect _canvasBounds;
    private TimeSpan _recentTime = TimeSpan.MinValue;

    public SceneRenderer(Scene scene, int width, int height)
    {
        _scene = scene;
        _graphics = s_dispatcher.Invoke(() => new Canvas(width, height));
        _canvasSize = _graphics.Size.ToSize(1);
        _canvasBounds = new Rect(_canvasSize);
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
                    item.ApplySetters(new(FrameNumber, this, layer.Scope));
                }
            }

            // IScopedRenderableをLayerに保持させる
            foreach (var item in end)
            {
                _objects[item.ZIndex] = item.Scope;

                foreach (var item2 in item.Operations)
                {
                    item2.EndingRender(item.Scope);
                }

                _clips.AddRange(item.Scope.OfType<Drawable>().Select(i => i.Bounds));
            }

            foreach (var item in begin)
            {
                _objects[item.ZIndex] = item.Scope;

                foreach (var item2 in item.Operations)
                {
                    item2.BeginningRender(item.Scope);
                }
            }

            Rect ClipToCanvasBounds(Rect rect)
            {
                return new Rect(
                    new Point(Math.Max(rect.Left, 0), Math.Max(rect.Top, 0)),
                    new Point(Math.Min(rect.Right, _canvasSize.Width), Math.Min(rect.Bottom, _canvasSize.Height)));
            }

            void AddClips(Rect rect1, Rect rect2)
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

            void Func(ReadOnlySpan<KeyValuePair<int, IScopedRenderable>> items, int start, int length)
            {
                for (int i = start; i < length; i++)
                {
                    var item = items[i];
                    foreach (var item2 in item.Value)
                    {
                        if (item2 is Drawable drawable)
                        {
                            Rect rect1 = drawable.Bounds;
                            drawable.Measure(_canvasSize);
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

            var reversed = new KeyValuePair<int, IScopedRenderable>[_objects.Count];
            _objects.CopyTo(reversed, 0);
            Array.Reverse(reversed);
            Func(reversed, 0, reversed.Length);

            using (Graphics.PushCanvas())
            {
                using var path = new SKPath();
                foreach (var item in _clips)
                {
                    path.AddRect(SKRect.Create(item.X, item.Y, item.Width, item.Height));
                }

                if (_clips.Count > 0)
                {
                    Graphics.Clear();
                }

                Graphics.ClipPath(path);

                foreach (var item in _objects)
                {
                    for (int i = item.Value.Count - 1; i >= 0; i--)
                    {
                        var item2 = item.Value[i];
                        if (!item2.IsVisible)
                        {
                            item.Value.RemoveAt(i);
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
