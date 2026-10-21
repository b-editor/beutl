using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.EllipseShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class EllipseShape : Shape
{
    public EllipseShape()
    {
        ScanProperties<EllipseShape>();
    }

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    public partial class Resource
    {
        private readonly EllipseGeometry _geometry = new();

        partial void PostReconcile(EllipseShape obj, CompositionContext context)
        {
            _geometry.Width.Value = Math.Max(Width, 0);
            _geometry.Height.Value = Math.Max(Height, 0);

            ReconcileGeometry(_geometry, context);
        }

        public override Geometry.Resource? GetGeometry() => GeometryResource;
    }
}
