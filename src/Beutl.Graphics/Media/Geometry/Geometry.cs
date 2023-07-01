using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;

using SkiaSharp;

namespace Beutl.Media;

public abstract class Geometry : Animatable, IAffectsRender
{
    public static readonly CoreProperty<PathFillType> FillTypeProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    private readonly GeometryContext _context = new();
    private PathCache _pathCache;
    private PathFillType _fillType;
    private ITransform? _transform;
    private bool _isDirty = true;

    static Geometry()
    {
        FillTypeProperty = ConfigureProperty<PathFillType, Geometry>(nameof(FillType))
            .Accessor(o => o.FillType, (o, v) => o.FillType = v)
            .Register();

        TransformProperty = ConfigureProperty<ITransform?, Geometry>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .Register();

        AffectsRender<Geometry>(FillTypeProperty, TransformProperty);
    }

    public Geometry()
    {
        Invalidated += OnInvalidated;
    }

    ~Geometry()
    {
        _context.Dispose();
        _pathCache.Invalidate();
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public PathFillType FillType
    {
        get => _fillType;
        set => SetAndRaise(FillTypeProperty, ref _fillType, value);
    }

    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    public Rect Bounds => GetNativeObject().TightBounds.ToGraphicsRect();

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Geometry
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.Invalidated?.Invoke(s, new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                    {
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;
                    }

                    if (e.NewValue is IAffectsRender newAffectsRender)
                    {
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                    }
                }
            });
        }
    }

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    public virtual void ApplyTo(IGeometryContext context)
    {
        if (Transform?.IsEnabled == true)
        {
            context.Transform(Transform.Value);
        }

        context.FillType = _fillType;
    }

    private void OnInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        _isDirty = true;
        _pathCache.Invalidate();
    }

    internal SKPath GetNativeObject()
    {
        if (_isDirty)
        {
            _context.Clear();
            ApplyTo(_context);
            _isDirty = false;
        }

        return _context.NativeObject;
    }

    public bool FillContains(Point point)
    {
        return PathContainsCore(GetNativeObject(), point);
    }

    public bool StrokeContains(IPen? pen, Point point)
    {
        if (pen == null) return false;

        float strokeWidth = pen.Thickness;
        StrokeAlignment alignment = pen.StrokeAlignment;

        if (!_pathCache.HasCacheFor(strokeWidth, alignment))
        {
            UpdatePathCache(strokeWidth, alignment);
        }

        return PathContainsCore(_pathCache.CachedStrokePath, point);
    }

    private void UpdatePathCache(float strokeWidth, StrokeAlignment alignment)
    {
        var strokePath = new SKPath();

        if (Math.Abs(strokeWidth) < float.Epsilon)
        {
            _pathCache.Cache(strokePath, strokeWidth, alignment, Bounds);
        }
        else
        {
            using (var paint = new SKPaint())
            {
                paint.IsStroke = true;
                SKPath fillPath = GetNativeObject();

                switch (alignment)
                {
                    case StrokeAlignment.Center:
                        paint.StrokeWidth = strokeWidth;
                        paint.GetFillPath(fillPath, strokePath);
                        break;
                    case StrokeAlignment.Inside or StrokeAlignment.Outside:
                        paint.StrokeWidth = strokeWidth * 2;
                        paint.GetFillPath(fillPath, strokePath);

                        using (var strokePathCopy = new SKPath(strokePath))
                        {
                            strokePathCopy.Op(
                                fillPath,
                                alignment == StrokeAlignment.Inside ? SKPathOp.Intersect : SKPathOp.Difference,
                                strokePath);
                        }
                        break;
                    default:
                        break;
                }
            }

            _pathCache.Cache(strokePath, strokeWidth, alignment, strokePath.TightBounds.ToGraphicsRect());
        }
    }

    private static bool PathContainsCore(SKPath? path, Point point)
    {
        return path is not null && path.Contains(point.X, point.Y);
    }

    public Rect GetRenderBounds(IPen? pen)
    {
        if (pen == null)
        {
            return Bounds;
        }
        else
        {
            float strokeWidth = pen.Thickness;
            StrokeAlignment alignment = pen.StrokeAlignment;

            if (!_pathCache.HasCacheFor(strokeWidth, alignment))
            {
                UpdatePathCache(strokeWidth, alignment);
            }

            return _pathCache.CachedGeometryRenderBounds;
        }
    }

    private struct PathCache
    {
        private float _cachedStrokeWidth;
        private StrokeAlignment _cachedStrokeAlignment;

        public const float Tolerance = float.Epsilon;

        public SKPath? CachedStrokePath { get; private set; }

        public Rect CachedGeometryRenderBounds { get; private set; }

        public readonly bool HasCacheFor(float strokeWidth, StrokeAlignment strokeAlignment)
        {
            return CachedStrokePath != null
                && Math.Abs(_cachedStrokeWidth - strokeWidth) < Tolerance
                && _cachedStrokeAlignment == strokeAlignment;
        }

        public void Cache(SKPath path, float strokeWidth, StrokeAlignment strokeAlignment, Rect geometryRenderBounds)
        {
            if (CachedStrokePath != path)
            {
                CachedStrokePath?.Dispose();
            }

            CachedStrokePath = path;
            CachedGeometryRenderBounds = geometryRenderBounds;
            _cachedStrokeWidth = strokeWidth;
            _cachedStrokeAlignment = strokeAlignment;
        }

        public void Invalidate()
        {
            CachedStrokePath?.Dispose();
            CachedStrokePath = null;
            CachedGeometryRenderBounds = default;
            _cachedStrokeWidth = default;
            _cachedStrokeAlignment = default;
        }
    }
}
