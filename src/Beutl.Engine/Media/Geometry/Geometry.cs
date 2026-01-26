using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Media;

public abstract partial class Geometry : EngineObject
{
    public Geometry()
    {
    }

    [Display(Name = nameof(Strings.FillType), ResourceType = typeof(Strings))]
    public IProperty<PathFillType> FillType { get; } = Property.Create<PathFillType>();

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
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
            if (_cachedStrokePath == null
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
