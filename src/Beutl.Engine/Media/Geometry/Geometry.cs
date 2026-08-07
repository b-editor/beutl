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

    public partial class Resource
    {
        private int? _capturedVersion;
        private GeometryContext? _cachedPath;
        private (Guid Identity, int Version)? _cachedPen;
        private SKPath? _cachedStrokePath;

        public Rect Bounds => GetCachedPath().TightBounds.ToGraphicsRect();

        /// <summary>
        /// Appends this geometry's outline to <paramref name="context"/>.
        /// </summary>
        /// <remarks>
        /// An override reads every parameter it needs from this resource, so it must not reach for
        /// <see cref="EngineObject.Resource.GetOriginal"/>.
        /// </remarks>
        public virtual void ApplyTo(IGeometryContext context)
        {
        }

        internal SKPath GetCachedPath()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_capturedVersion != Version || _cachedPath == null)
            {
                // A throwing ApplyTo must not leave a half-built path behind the version guard, so the
                // fields are replaced only once the new path is complete.
                var built = new GeometryContext { FillType = FillType };
                try
                {
                    ApplyTo(built);
                    if (Transform != null)
                    {
                        built.Transform(Transform.Matrix);
                    }
                }
                catch
                {
                    built.Dispose();
                    throw;
                }

                _cachedStrokePath?.Dispose();
                _cachedStrokePath = null;
                _cachedPath?.Dispose();
                _cachedPath = built;
                _capturedVersion = Version;
            }

            return _cachedPath.NativeObject;
        }

        internal SKPath GetCachedStrokePath(Pen.Resource pen)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            // GetOriginal() is null for every detached pen, so keying on it makes any two of them compare equal.
            Guid penIdentity = EngineResourceIdentity.Of(pen);
            if (_capturedVersion != Version
                || _cachedPath == null
                || _cachedStrokePath == null
                || _cachedPen == null
                || _cachedPen?.Identity != penIdentity
                || _cachedPen?.Version != pen.Version)
            {
                _cachedStrokePath?.Dispose();
                _cachedPen = (penIdentity, pen.Version);
                _cachedStrokePath = PenHelper.CreateStrokePath(GetCachedPath(), pen, Bounds);
            }

            return _cachedStrokePath;
        }

        // Version keys both the fill/stroke path cache and any render node's (resource, Version) snapshot,
        // so bumping it invalidates both. Stale paths are disposed lazily (a render thread may hold the old one).
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
