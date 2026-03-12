using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.GeometryShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class GeometryShape : Shape
{
    public GeometryShape()
    {
        ScanProperties<GeometryShape>();
        Data.CurrentValue = new PathGeometry();
    }

    [Display(Name = nameof(GraphicsStrings.GeometryShape_Data), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Geometry?> Data { get; } = Property.Create<Geometry?>();

    public partial class Resource
    {
        public override Geometry.Resource? GetGeometry()
        {
            return Data;
        }
    }
}
