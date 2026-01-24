using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Rectangle), ResourceType = typeof(Strings))]
public sealed partial class RectGeometry : Geometry
{
    public RectGeometry()
    {
        ScanProperties<RectGeometry>();
    }

    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
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

        context.MoveTo(new Point(0, 0));
        context.LineTo(new Point(width, 0));
        context.LineTo(new Point(width, height));
        context.LineTo(new Point(0, height));
        context.LineTo(new Point(0, 0));
        context.Close();
    }
}
