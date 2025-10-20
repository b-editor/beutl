using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.RoundedRect), ResourceType = typeof(Strings))]
public sealed partial class RoundedRectShape : Shape
{
    public RoundedRectShape()
    {
        ScanProperties<RoundedRectShape>();
    }

    [Display(Name = nameof(Strings.CornerRadius), ResourceType = typeof(Strings))]
    [Range(typeof(CornerRadius), "0", "max")]
    public IProperty<CornerRadius> CornerRadius { get; } = Property.CreateAnimatable<CornerRadius>();

    [Range(0, 100)]
    [Display(Name = nameof(Strings.Smoothing), ResourceType = typeof(Strings))]
    public IProperty<float> Smoothing { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        private readonly RoundedRectGeometry _geometry = new();
        private RoundedRectGeometry.Resource? _geometryResource;

        partial void PostUpdate(RoundedRectShape obj, RenderContext context)
        {
            _geometry.Width.CurrentValue = Math.Max(Width, 0);
            _geometry.Height.CurrentValue = Math.Max(Height, 0);
            _geometry.CornerRadius.CurrentValue = CornerRadius;
            _geometry.Smoothing.CurrentValue = Math.Clamp(Smoothing, 0, 100);

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
