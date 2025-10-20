using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.Ellipse), ResourceType = typeof(Strings))]
public sealed partial class EllipseShape : Shape
{
    public partial class Resource
    {
        private readonly EllipseGeometry _geometry = new();
        private EllipseGeometry.Resource? _geometryResource;

        partial void PostUpdate(EllipseShape obj, RenderContext context)
        {
            _geometry.Width.CurrentValue = Math.Max(Width, 0);
            _geometry.Height.CurrentValue = Math.Max(Height, 0);

            if (_geometryResource is null)
            {
                _geometryResource = _geometry.ToResource(context);
                Version++;
            }
            else
            {
                if (_geometryResource.GetOriginal() != _geometry)
                {
                    _geometryResource = _geometry.ToResource(context);
                    Version++;
                }
                else
                {
                    var oldVersion = _geometryResource.Version;
                    var _ = false;
                    _geometryResource.Update(_geometry, context, ref _);
                    if (oldVersion != _geometryResource.Version)
                    {
                        Version++;
                    }
                }
            }
        }

        public override Geometry.Resource? GetGeometry() => _geometryResource;
    }
}
