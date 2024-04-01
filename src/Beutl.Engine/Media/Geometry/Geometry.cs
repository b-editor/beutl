using System.ComponentModel;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;

using SkiaSharp;

using PathOptions = (float Thickness, Beutl.Media.StrokeAlignment Alignment, Beutl.Media.StrokeCap StrokeCap, Beutl.Media.StrokeJoin StrokeJoin, float MiterLimit);

namespace Beutl.Media;

public abstract class Geometry : Animatable, IAffectsRender
{
    public static readonly CoreProperty<PathFillType> FillTypeProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    private GeometryContext? _context;
    private PathCache _pathCache;
    private PathFillType _fillType;
    private ITransform? _transform;
    private Rect _bounds;
    private bool _isDirty = true;
    private int _version;

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
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    ~Geometry()
    {
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

    public Rect Bounds
    {
        get
        {
            _ = GetNativeObject();
            return _bounds;
        }
    }

    internal int Version
    {
        get
        {
            _ = GetNativeObject();
            return _version;
        }
    }

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
        RaiseInvalidated(e);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    public virtual void ApplyTo(IGeometryContext context)
    {
    }

    private void OnInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        _isDirty = true;
        _context = null;
        _pathCache.Invalidate();
    }

    internal SKPath GetNativeObject()
    {
        return GetContext().NativeObject;
    }

    internal GeometryContext GetContext()
    {
        if (_isDirty || _context == null)
        {
            var context = new GeometryContext
            {
                FillType = _fillType
            };
            ApplyTo(context);
            if (Transform?.IsEnabled == true)
            {
                context.Transform(Transform.Value);
            }

            _bounds = context.Bounds;

            _isDirty = false;
            unchecked
            {
                _version++;
            }

            _context = context;
        }

        return _context;
    }

    public bool FillContains(Point point)
    {
        return PathContainsCore(GetNativeObject(), point);
    }

    public bool StrokeContains(IPen? pen, Point point)
    {
        if (pen == null) return false;

        if (!_pathCache.HasCacheFor(pen))
        {
            UpdatePathCache(pen);
        }

        return PathContainsCore(_pathCache.CachedStrokePath, point);
    }

    internal SKPath? GetStrokePath(IPen? pen)
    {
        if (pen == null) return null;

        if (!_pathCache.HasCacheFor(pen))
        {
            UpdatePathCache(pen);
        }

        return _pathCache.CachedStrokePath;
    }

    private void UpdatePathCache(IPen pen)
    {
        Rect bounds = Bounds;

        if (Math.Abs(pen.Thickness) < float.Epsilon)
        {
            _pathCache.Cache(new SKPath(), pen, bounds);
        }
        else
        {
            SKPath fillPath = GetNativeObject();
            SKPath strokePath = PenHelper.CreateStrokePath(fillPath, pen, bounds);

            _pathCache.Cache(strokePath, pen, strokePath.TightBounds.ToGraphicsRect());
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
            if (!_pathCache.HasCacheFor(pen))
            {
                UpdatePathCache(pen);
            }

            return _pathCache.CachedGeometryRenderBounds;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Transform as IAnimatable)?.ApplyAnimations(clock);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public int GetVersion() => Version;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public SKPath GetNativeObjectPublic() => GetNativeObject();

    [EditorBrowsable(EditorBrowsableState.Never)]
    public Rect GetCurrentBounds() => _bounds;

    private struct PathCache
    {
        private IPen? _cachedPen;

        public SKPath? CachedStrokePath { get; private set; }

        public Rect CachedGeometryRenderBounds { get; private set; }

        public readonly bool HasCacheFor(IPen pen)
        {
            return CachedStrokePath != null
                && EqualityComparer<IPen?>.Default.Equals(_cachedPen, pen);
        }

        public void Cache(SKPath path, IPen pen, Rect geometryRenderBounds)
        {
            if (CachedStrokePath != path)
            {
                CachedStrokePath?.Dispose();
            }

            CachedStrokePath = path;
            CachedGeometryRenderBounds = geometryRenderBounds;
            _cachedPen = (pen as IMutablePen)?.ToImmutable() ?? pen;
        }

        public void Invalidate()
        {
            SKPath? tmp = CachedStrokePath;
            CachedStrokePath = null;
            tmp?.Dispose();
            CachedGeometryRenderBounds = default;
            _cachedPen = null;
        }
    }
}
