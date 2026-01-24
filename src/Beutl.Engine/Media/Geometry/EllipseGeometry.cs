using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Ellipse), ResourceType = typeof(Strings))]
public sealed partial class EllipseGeometry : Geometry
{
    public EllipseGeometry()
    {
        ScanProperties<EllipseGeometry>();
    }

    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(IGeometryContext context, Geometry.Resource resource)
    {
        base.ApplyTo(context, resource);
        var r = (Resource)resource;
        float width = r.Width;
        float height = r.Height;
        if (float.IsInfinity(width))
            width = 0;

        if (float.IsInfinity(height))
            height = 0;

        float radiusX = width / 2;
        float radiusY = height / 2;
        var radius = new Size(radiusX, radiusY);

        context.MoveTo(new Point(radiusX, 0));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, height));
        context.ArcTo(radius, 0, true, false, new Point(radiusX, 0));
        context.Close();
    }
}
