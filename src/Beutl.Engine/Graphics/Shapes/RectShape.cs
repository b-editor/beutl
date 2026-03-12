using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.RectShape), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RectShape : Shape
{
    public RectShape()
    {
        ScanProperties<RectShape>();
    }

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable<float>(100);

    public partial class Resource
    {
        private readonly RectGeometry _geometry = new();
        private RectGeometry.Resource? _geometryResource;

        partial void PostUpdate(RectShape obj, CompositionContext context)
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
