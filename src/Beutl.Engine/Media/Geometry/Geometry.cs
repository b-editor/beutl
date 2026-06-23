using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Media;

public sealed partial class FallbackGeometry : Geometry, IFallback;

[FallbackType(typeof(FallbackGeometry))]
public abstract partial class Geometry : EngineObject
{
    public Geometry()
    {
    }

    [Display(Name = nameof(GraphicsStrings.Geometry_FillType), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PathFillType> FillType { get; } = Property.Create<PathFillType>();

    [Display(Name = nameof(GraphicsStrings.Transform), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>(null);

    public virtual void ApplyTo(IGeometryContext context, Resource resource)
    {
    }

    public partial class Resource
    {
        private int? _capturedVersion;
        private GeometryContext? _cachedPath;
        private (Pen.Resource Resource, int Version)? _cachedPen;
        private SKPath? _cachedStrokePath;

        public Rect Bounds => GetCachedPath().TightBounds.ToGraphicsRect();

        internal SKPath GetCachedPath()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_capturedVersion != Version || _cachedPath == null)
            {
                _capturedVersion = Version;
                _cachedStrokePath?.Dispose();
                _cachedStrokePath = null;
                _cachedPath?.Dispose();
                var geometry = GetOriginal();

                _cachedPath = new GeometryContext { FillType = FillType };
                geometry.ApplyTo(_cachedPath, this);
                if (Transform != null)
                {
                    _cachedPath.Transform(Transform.Matrix);
                }
            }

            return _cachedPath.NativeObject;
        }

        internal SKPath GetCachedStrokePath(Pen.Resource pen)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_capturedVersion != Version
                || _cachedPath == null
                || _cachedStrokePath == null
                || _cachedPen == null
                || _cachedPen?.Resource.GetOriginal() != pen.GetOriginal()
                || _cachedPen?.Version != pen.Version)
            {
                _cachedStrokePath?.Dispose();
                _cachedPen = (pen, pen.Version);
                _cachedStrokePath = PenHelper.CreateStrokePath(GetCachedPath(), pen, Bounds);
            }

            return _cachedStrokePath;
        }

        // Signals that the underlying geometry's path was mutated in place, by bumping Version.
        //
        // Version is the invalidation key for two cache layers, and composition's Update path is what
        // normally bumps it. A caller that rewrites the path without going through that path (e.g.
        // FormattedText reusing a per-glyph slot via SetSKPath) leaves Version unchanged, so both layers
        // keep serving the previous glyph:
        //   * this resource's own Version-keyed fill/stroke path cache (GetCachedPath / GetCachedStrokePath), and
        //   * any render node that captured a (resource, Version) snapshot and skips redraw while it looks
        //     unchanged (GeometryRenderNode.Update -> ResourceExtension.Compare), which in turn keeps a stale
        //     rasterized RenderNodeCache tile alive.
        // Bumping Version invalidates both: the next GetCachedPath rebuilds, and the render node observes a
        // new Version, reports HasChanges, redraws, and resets its cache-eligibility counter. Setting only
        // _capturedVersion = null would rebuild this resource's own cache but leave the render node — and its
        // rasterized tile — stale.
        //
        // Disposing the now-stale cached paths is intentionally deferred to the next GetCachedPath /
        // GetCachedStrokePath (the same as the ordinary Version-change rebuild path) rather than freed here:
        // a render thread may still hold the previously returned SKPath, and PostDispose covers the case
        // where no further access occurs.
        internal void InvalidateCachedPaths()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Version++;
        }

        public Rect GetRenderBounds(Pen.Resource? pen)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (pen == null)
            {
                return Bounds;
            }
            else
            {
                var strokePath = GetCachedStrokePath(pen);
                return strokePath.TightBounds.ToGraphicsRect();
            }
        }

        public bool FillContains(Point point)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return PathContainsCore(GetCachedPath(), point);
        }

        private static bool PathContainsCore(SKPath? path, Point point)
        {
            return path is not null && path.Contains(point.X, point.Y);
        }

        public bool StrokeContains(Pen.Resource? pen, Point point)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (pen == null) return false;

            SKPath? strokePath = GetCachedStrokePath(pen);

            return PathContainsCore(strokePath, point);
        }

        partial void PostDispose(bool disposing)
        {
            _cachedPath?.Dispose();

            _cachedStrokePath?.Dispose();

            _cachedPen = null;
        }
    }
}
