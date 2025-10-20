using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.GeometryShape), ResourceType = typeof(Strings))]
public sealed partial class GeometryShape : Shape
{
    public GeometryShape()
    {
        ScanProperties<GeometryShape>();
    }

    public IProperty<Geometry?> Data { get; } = Property.Create<Geometry?>();

    public partial class Resource
    {
        public override Geometry.Resource? GetGeometry()
        {
            return Data;
        }
    }
}
