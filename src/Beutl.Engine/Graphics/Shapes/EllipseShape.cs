using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.Ellipse), ResourceType = typeof(Strings))]
public sealed partial class EllipseShape : Shape
{
    private EllipseGeometry? _geometry;

    static EllipseShape()
    {
        WidthProperty.OverrideDefaultValue<EllipseShape>(0f);
        HeightProperty.OverrideDefaultValue<EllipseShape>(0f);
        AffectsGeometry<EllipseShape>(WidthProperty, HeightProperty);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new EllipseGeometry();
        _geometry.Width = Math.Max(Width, 0);
        _geometry.Height = Math.Max(Height, 0);
        return _geometry;
    }
}
