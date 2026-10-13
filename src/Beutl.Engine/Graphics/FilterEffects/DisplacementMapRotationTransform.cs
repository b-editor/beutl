using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    public DisplacementMapRotationTransform()
    {
        ScanProperties<DisplacementMapRotationTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Rotation { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.CenterX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.CenterY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>(0);

    public partial class Resource
    {
        public override void ApplyTo(
            Brush.Resource displacementMap, GradientSpreadMethod spreadMethod,
            DisplacementMapChannel channel, bool signed, FilterEffectContext context)
            => ApplyCore(
                context,
                displacementMap,
                spreadMethod,
                channel,
                signed,
                DrawableMapTransformKind.Rotation,
                vector: default,
                angle: MathUtilities.Deg2Rad(Rotation),
                center: new Vector2(CenterX, CenterY));
    }
}
