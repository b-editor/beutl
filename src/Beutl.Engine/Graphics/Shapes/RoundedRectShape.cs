using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.RoundedRect), ResourceType = typeof(Strings))]
public sealed partial class RoundedRectShape : Shape
{
    public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
    public static readonly CoreProperty<float> SmoothingProperty;
    private CornerRadius _cornerRadius;
    private float _smoothing;
    private RoundedRectGeometry? _geometry;

    static RoundedRectShape()
    {
        WidthProperty.OverrideDefaultValue<RoundedRectShape>(0f);
        HeightProperty.OverrideDefaultValue<RoundedRectShape>(0f);

        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectShape>(nameof(CornerRadius))
            .Accessor(o => o.CornerRadius, (o, v) => o.CornerRadius = v)
            .DefaultValue(new CornerRadius())
            .Register();

        SmoothingProperty = ConfigureProperty<float, RoundedRectShape>(nameof(Smoothing))
            .Accessor(o => o.Smoothing, (o, v) => o.Smoothing = v)
            .DefaultValue(0)
            .Register();

        AffectsGeometry<RoundedRectShape>(
            WidthProperty, HeightProperty,
            CornerRadiusProperty, SmoothingProperty);
    }

    [Display(Name = nameof(Strings.CornerRadius), ResourceType = typeof(Strings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set => SetAndRaise(CornerRadiusProperty, ref _cornerRadius, value);
    }

    [Range(0, 100)]
    [Display(Name = nameof(Strings.Smoothing), ResourceType = typeof(Strings))]
    public float Smoothing
    {
        get => _smoothing;
        set => SetAndRaise(SmoothingProperty, ref _smoothing, value);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new RoundedRectGeometry();
        _geometry.Width = Math.Max(Width, 0);
        _geometry.Height = Math.Max(Height, 0);
        _geometry.CornerRadius = CornerRadius;
        _geometry.Smoothing = Smoothing;
        return _geometry;
    }
}
