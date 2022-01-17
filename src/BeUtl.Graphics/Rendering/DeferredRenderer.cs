using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BeUtl.Graphics;
using BeUtl.Threading;

using SkiaSharp;

namespace BeUtl.Rendering;

public class DeferredRenderer : IRenderer
{
    internal static readonly Dispatcher s_dispatcher = Dispatcher.Spawn();
    private readonly SortedDictionary<int, ILayerScope> _objects = new();
    private readonly List<Rect> _clips = new();
    private readonly Canvas _graphics;
    private readonly Rect _canvasBounds;

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

    public virtual void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public virtual IRenderer.RenderResult Render()
    {
        Dispatcher.VerifyAccess();
        if (!IsRendering)
        {
            IsRendering = true;
            var objects = new KeyValuePair<int, ILayerScope>[_objects.Count];
            _objects.CopyTo(objects, 0);
            Func(objects, 0, objects.Length);

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

                foreach (KeyValuePair<int, ILayerScope> item in objects)
                {
                    for (int ii = item.Value.Count - 1; ii >= 0; ii--)
                    {
                        IRenderable item2 = item.Value[ii];
                        if (!item2.IsVisible)
                        {
                            //item.Value.RemoveAt(ii);
                        }
                        else if (item2 is Drawable drawable && drawable.IsDirty)
                        {
                            item2.Render(this);
                        }
                    }
                }

                _clips.Clear();
            }

            IsRendering = false;
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    // 変更されているオブジェクトのBoundsを_clipsに追加して、
    // そのオブジェクトが影響を与えるオブジェクトも同様の処理をする
    private void Func(ReadOnlySpan<KeyValuePair<int, ILayerScope>> items, int start, int length)
    {
        for (int i = length - 1; i >= start; i--)
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
                            AddDirtyRects(rect1, rect2);
                            drawable.InvalidateVisual();

                            //Func(items, 0, i);
                            Func(items, i + 1, items.Length);
                        }
                        else if (HitTestClips(rect1, rect2))
                        {
                            drawable.InvalidateVisual();
                        }
                    }
                }
            }
        }
    }

    // _clipsにrect1, rect2を追加する
    protected void AddDirtyRect(Rect rect1)
    {
        if (!rect1.IsEmpty)
        {
            if (!_canvasBounds.Contains(rect1))
            {
                rect1 = ClipToCanvasBounds(rect1);
            }
            _clips.Add(rect1);
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

    public async void Invalidate()
    {
        if (RenderInvalidated != null)
        {
            IRenderer.RenderResult result = await Dispatcher.InvokeAsync(() => Render());
            RenderInvalidated.Invoke(this, result);
            result.Bitmap.Dispose();
        }
    }
}
