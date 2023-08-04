using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Rendering.Cache;

namespace Beutl.Rendering;

public sealed class RenderLayer : IDisposable
{
    private class Entry : IDisposable
    {
        public Entry(INode node)
        {
            Node = node;
            IsDirty = true;
        }

        ~Entry()
        {
            Dispose();
        }

        public INode Node { get; }

        public bool IsDirty { get; set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Node.Dispose();
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    // このテーブルは本描画するときに、自分のレイヤー以外のものを削除する。
    private readonly ConditionalWeakTable<Renderable, Entry> _cache = new();

    private readonly List<Entry> _currentFrame = new(1);
    private readonly RenderScene _renderScene;

    public RenderLayer(RenderScene renderScene)
    {
        _renderScene = renderScene;
    }

    public void Clear()
    {
        _currentFrame.Clear();
    }

    public void UpdateAll(IReadOnlyList<Renderable> elements)
    {
        _currentFrame.Clear();
        _currentFrame.EnsureCapacity(elements.Count);

        // Todo: Drawable, Renderableを統合する予定
        foreach (Renderable element in elements)
        {
            if (!_cache.TryGetValue(element, out Entry? entry))
            {
                if (element is Drawable drawable)
                {
                    entry = new Entry(new DrawableNode(drawable));
                    _cache.Add(element, entry);

                    var weakRef = new WeakReference<Entry>(entry);
                    EventHandler<RenderInvalidatedEventArgs>? handler = null;
                    handler = (_, _) =>
                    {
                        if (weakRef.TryGetTarget(out Entry? obj))
                        {
                            obj.IsDirty = true;
                        }
                        else
                        {
                            element.Invalidated -= handler;
                        }
                    };
                    element.Invalidated += handler;
                }
                else if (element is Sound sound)
                {
                    entry = new Entry(new SoundNode(sound));
                    _cache.Add(element, entry);
                }
            }

            if (entry != null)
            {
                if (entry.IsDirty
                    && entry.Node is DrawableNode { Drawable: var drawable } drawableNode)
                {
                    // DeferredCanvasを作成し、記録
                    var canvas = new DeferradCanvas(drawableNode, _renderScene.Size);
                    drawable.Render(canvas);
                    entry.IsDirty = false;
                }

                _currentFrame.Add(entry);
            }
        }
    }

    public void ClearAllNodeCache(RenderCacheContext? context)
    {
        foreach (KeyValuePair<Renderable, Entry> item in _cache)
        {
            if (item.Value.Node is IGraphicNode graphicNode)
                context?.ClearCache(graphicNode);

            item.Value.Dispose();
        }

        _cache.Clear();
    }

    public void Render(ImmediateCanvas canvas)
    {
        foreach (Entry? entry in CollectionsMarshal.AsSpan(_currentFrame))
        {
            if (entry.Node is DrawableNode dnode)
            {
                Drawable element = dnode.Drawable;
                if (entry.IsDirty)
                {
                    var dcanvas = new DeferradCanvas(dnode, _renderScene.Size);
                    element.Render(dcanvas);
                    entry.IsDirty = false;
                }

                canvas.DrawNode(dnode);

                canvas.GetCacheContext()?.MakeCache(dnode, canvas);
            }
        }
    }

    public void Render(Audio.Audio audio)
    {
        foreach (Entry? entry in CollectionsMarshal.AsSpan(_currentFrame))
        {
            if (entry.Node is SoundNode snode)
            {
                snode.Sound.Render(audio);
            }
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<Renderable, Entry> item in _cache)
        {
            item.Value.Dispose();
        }

        _cache.Clear();
        _currentFrame.Clear();
    }
}
