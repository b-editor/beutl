using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

public sealed class Pen : Animatable, IMutablePen, IEquatable<IPen?>
{
    public static readonly CoreProperty<IBrush?> BrushProperty;
    public static readonly CoreProperty<CoreList<float>?> DashArrayProperty;
    public static readonly CoreProperty<float> DashOffsetProperty;
    public static readonly CoreProperty<float> ThicknessProperty;
    public static readonly CoreProperty<float> MiterLimitProperty;
    public static readonly CoreProperty<StrokeCap> StrokeCapProperty;
    public static readonly CoreProperty<StrokeJoin> StrokeJoinProperty;
    public static readonly CoreProperty<StrokeAlignment> StrokeAlignmentProperty;
    private IBrush? _brush;
    private CoreList<float>? _dashArray;
    private float _dashOffset;
    private float _thickness = 1;
    private float _miterLimit = 10;
    private StrokeCap _strokeCap = StrokeCap.Flat;
    private StrokeJoin _strokeJoin = StrokeJoin.Miter;
    private StrokeAlignment _strokeAlignment = StrokeAlignment.Center;

    static Pen()
    {
        BrushProperty = ConfigureProperty<IBrush?, Pen>(nameof(Brush))
            .Accessor(o => o.Brush, (o, v) => o.Brush = v)
            .Register();

        DashArrayProperty = ConfigureProperty<CoreList<float>?, Pen>(nameof(DashArray))
            .Accessor(o => o.DashArray, (o, v) => o.DashArray = v)
            .Register();

        DashOffsetProperty = ConfigureProperty<float, Pen>(nameof(DashOffset))
            .Accessor(o => o.DashOffset, (o, v) => o.DashOffset = v)
            .Register();

        ThicknessProperty = ConfigureProperty<float, Pen>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .DefaultValue(1)
            .Register();

        MiterLimitProperty = ConfigureProperty<float, Pen>(nameof(MiterLimit))
            .Accessor(o => o.MiterLimit, (o, v) => o.MiterLimit = v)
            .DefaultValue(10)
            .Register();

        StrokeCapProperty = ConfigureProperty<StrokeCap, Pen>(nameof(StrokeCap))
            .Accessor(o => o.StrokeCap, (o, v) => o.StrokeCap = v)
            .DefaultValue(StrokeCap.Flat)
            .Register();

        StrokeJoinProperty = ConfigureProperty<StrokeJoin, Pen>(nameof(StrokeJoin))
            .Accessor(o => o.StrokeJoin, (o, v) => o.StrokeJoin = v)
            .DefaultValue(StrokeJoin.Miter)
            .Register();

        StrokeAlignmentProperty = ConfigureProperty<StrokeAlignment, Pen>(nameof(StrokeAlignment))
            .Accessor(o => o.StrokeAlignment, (o, v) => o.StrokeAlignment = v)
            .DefaultValue(StrokeAlignment.Center)
            .Register();
    }

    [Display(Name = nameof(Strings.Brush), ResourceType = typeof(Strings))]
    public IBrush? Brush
    {
        get => _brush;
        set => SetAndRaise(BrushProperty, ref _brush, value);
    }

    [Display(Name = nameof(Strings.Pen_DashArray), ResourceType = typeof(Strings))]
    public CoreList<float>? DashArray
    {
        get => _dashArray;
        set => SetAndRaise(DashArrayProperty, ref _dashArray, value);
    }

    [Display(Name = nameof(Strings.Pen_DashOffset), ResourceType = typeof(Strings))]
    public float DashOffset
    {
        get => _dashOffset;
        set => SetAndRaise(DashOffsetProperty, ref _dashOffset, value);
    }

    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    [Display(Name = nameof(Strings.Pen_MiterLimit), ResourceType = typeof(Strings))]
    public float MiterLimit
    {
        get => _miterLimit;
        set => SetAndRaise(MiterLimitProperty, ref _miterLimit, value);
    }

    [Display(Name = nameof(Strings.Pen_StrokeCap), ResourceType = typeof(Strings))]
    public StrokeCap StrokeCap
    {
        get => _strokeCap;
        set => SetAndRaise(StrokeCapProperty, ref _strokeCap, value);
    }

    [Display(Name = nameof(Strings.Pen_StrokeJoin), ResourceType = typeof(Strings))]
    public StrokeJoin StrokeJoin
    {
        get => _strokeJoin;
        set => SetAndRaise(StrokeJoinProperty, ref _strokeJoin, value);
    }

    [Display(Name = nameof(Strings.Pen_StrokeAlignment), ResourceType = typeof(Strings))]
    public StrokeAlignment StrokeAlignment
    {
        get => _strokeAlignment;
        set => SetAndRaise(StrokeAlignmentProperty, ref _strokeAlignment, value);
    }

    IReadOnlyList<float>? IPen.DashArray => _dashArray;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        if (_brush is IAnimatable animatable)
        {
            animatable.ApplyAnimations(clock);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        switch (args)
        {
            case CorePropertyChangedEventArgs<IBrush?> { PropertyName: nameof(Brush) } args1:
                if (args1.OldValue is IAffectsRender oldAffectRender)
                {
                    oldAffectRender.Invalidated -= OnAffectRenderInvalidated;
                }

                if (args1.NewValue is IAffectsRender newAffectRender)
                {
                    newAffectRender.Invalidated += OnAffectRenderInvalidated;
                }

                goto RaiseInvalidated;

            case CorePropertyChangedEventArgs<CoreList<float>?> { PropertyName: nameof(DashArray) } args2:
                if (args2.OldValue is { })
                {
                    args2.OldValue.CollectionChanged -= OnDashArrayCollectionChanged;
                }

                if (args2.NewValue is { })
                {
                    args2.NewValue.CollectionChanged += OnDashArrayCollectionChanged;
                }

                goto RaiseInvalidated;

            case { PropertyName: nameof(DashOffset) }:
            case { PropertyName: nameof(Thickness) }:
            case { PropertyName: nameof(MiterLimit) }:
            case { PropertyName: nameof(StrokeCap) }:
            case { PropertyName: nameof(StrokeJoin) }:
            case { PropertyName: nameof(StrokeAlignment) }:
            RaiseInvalidated:
                Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this, args.PropertyName));
                break;

            default:
                break;
        }
    }

    private void OnDashArrayCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(DashArray!));
    }

    private void OnAffectRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    public IPen ToImmutable()
    {
        return new ImmutablePen(
            (Brush as IMutableBrush)?.ToImmutable() ?? Brush,
            DashArray?.ToArray(),
            DashOffset,
            Thickness,
            MiterLimit,
            StrokeCap,
            StrokeJoin,
            StrokeAlignment);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as IPen);
    }

    public bool Equals(IPen? other)
    {
        return other is not null
            && EqualityComparer<IBrush?>.Default.Equals(Brush, other.Brush)
            && ((DashArray != null && other.DashArray != null && DashArray.SequenceEqual(other.DashArray))
            || DashArray == null && other.DashArray == null)
            && DashOffset == other.DashOffset
            && Thickness == other.Thickness
            && MiterLimit == other.MiterLimit
            && StrokeCap == other.StrokeCap
            && StrokeJoin == other.StrokeJoin
            && StrokeAlignment == other.StrokeAlignment;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Brush, DashArray, DashOffset, Thickness, MiterLimit, StrokeCap, StrokeJoin, StrokeAlignment);
    }
}
