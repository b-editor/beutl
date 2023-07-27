using System.Diagnostics;
using System.Runtime.CompilerServices;

using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Rendering.Cache;

public class RenderCacheContext
{
    private readonly ConditionalWeakTable<IGraphicNode, RenderCache> _table = new();

    public RenderCache GetCache(IGraphicNode node)
    {
        return _table.GetValue(node, key => new RenderCache(key));
    }

    public bool CanCacheRecursive(IGraphicNode node)
    {
        RenderCache cache = GetCache(node);
        if (!cache.CanCache())
            return false;

        if (node is ContainerNode containerNode)
        {
            foreach (IGraphicNode item in containerNode.Children)
            {
                if (!CanCacheRecursive(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void MakeCache(IGraphicNode node, IImmediateCanvasFactory factory)
    {
        RenderCache cache = GetCache(node);
        if (CanCacheRecursive(node))
        {
            if (!cache.IsCached)
            {
                // nodeをキャッシュ
                Rect bounds = (node as ISupportRenderCache)?.TransformBoundsForCache(cache) ?? node.Bounds;
                if(node is FilterEffectNode)
                {

                }

                SKSurface surface = factory.CreateRenderTarget((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));

                using (ImmediateCanvas canvas = factory.CreateCanvas(surface, true))
                {
                    using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
                    {
                        if (node is ISupportRenderCache supportRenderCache)
                        {
                            supportRenderCache.RenderForCache(canvas, cache);
                        }
                        else
                        {
                            node.Render(canvas);
                        }
                    }
                }

                cache.StoreCache(Ref<SKSurface>.Create(surface), bounds);

                Debug.WriteLine($"[RenderCache:Created] '{node}'");
            }
        }
        else if (node is ContainerNode containerNode)
        {
            cache.Invalidate();
            foreach (IGraphicNode item in containerNode.Children)
            {
                MakeCache(item, factory);
            }
        }
    }
}
