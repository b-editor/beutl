using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.Rectangle), ResourceType = typeof(Strings))]
public sealed partial class RectShape : Shape
{
    public partial class Resource
    {
        private readonly RectGeometry _geometry = new();
        private RectGeometry.Resource? _geometryResource;

        partial void PostUpdate(RectShape obj, RenderContext context)
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
                    var oldGeometry = _geometryResource;
                    _geometryResource = _geometry.ToResource(context);
                    oldGeometry.Dispose();
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

        partial void PostDispose(bool disposing)
        {
            _geometryResource?.Dispose();
        }

        public override Geometry.Resource? GetGeometry() => _geometryResource;
    }
}
