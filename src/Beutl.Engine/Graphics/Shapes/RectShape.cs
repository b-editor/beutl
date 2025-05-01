using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.Rectangle), ResourceType = typeof(Strings))]
public sealed partial class RectShape : Shape
{
    private RectGeometry? _geometry;

    static RectShape()
    {
        WidthProperty.OverrideDefaultValue<RectShape>(0f);
        HeightProperty.OverrideDefaultValue<RectShape>(0f);
        AffectsGeometry<RectShape>(WidthProperty, HeightProperty);
    }

    protected override Geometry CreateGeometry()
    {
        _geometry ??= new RectGeometry();
        _geometry.Width = Math.Max(Width, 0);
        _geometry.Height = Math.Max(Height, 0);
        return _geometry;
    }
}
