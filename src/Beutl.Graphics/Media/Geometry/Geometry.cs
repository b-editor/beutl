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
            if (_isDirty)
            {
                _context.Clear();
                ApplyTo(_context);
                _isDirty = false;
            }

            return _context.Bounds;
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
}
