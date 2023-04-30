using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public sealed partial class RoundedRectShape : Shape
{
    public static readonly CoreProperty<CornerRadius> CornerRadiusProperty;
    private CornerRadius _cornerRadius;
    private RoundedRectGeometry? _geometry;

    static RoundedRectShape()
    {
        WidthProperty.OverrideDefaultValue<RoundedRectShape>(0f);
        HeightProperty.OverrideDefaultValue<RoundedRectShape>(0f);

        CornerRadiusProperty = ConfigureProperty<CornerRadius, RoundedRectShape>(nameof(CornerRadius))
            .Accessor(o => o.CornerRadius, (o, v) => o.CornerRadius = v)
            .DefaultValue(new CornerRadius())
            .Register();

        AffectsGeometry<RoundedRectShape>(WidthProperty, HeightProperty, CornerRadiusProperty);
    }

    [Display(Name = nameof(Strings.CornerRadius), ResourceType = typeof(Strings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set => SetAndRaise(CornerRadiusProperty, ref _cornerRadius, value);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new RoundedRectGeometry();
        _geometry.Width = Math.Max(Width, 0);
        _geometry.Height = Math.Max(Height, 0);
        _geometry.CornerRadius = CornerRadius;
        return _geometry;
    }
}
