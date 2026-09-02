using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float uAngle;
        uniform float2 uPivot;

        """
        + DisplacementSamplingSource
        + """


        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float disp = getDisplacement(dispColor);
            float2 offset = float2(cos(uAngle * disp), sin(uAngle * disp));

            float2 uv = coord - uPivot;
            float2 rotated = float2(uv.x * offset.x - uv.y * offset.y, uv.x * offset.y + uv.y * offset.x);
            uv = rotated + uPivot;
            return src.eval(uv);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

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
        internal override void ApplyTo(
            Brush.Resource displacementMap, GradientSpreadMethod spreadMethod,
            DisplacementMapChannel channel, bool signed, FilterEffectContext context)
        {
            if (TryApplyDrawableMap(
                    context,
                    displacementMap,
                    spreadMethod,
                    channel,
                    signed,
                    DrawableMapTransformKind.Rotation,
                    vector: default,
                    angle: MathUtilities.Deg2Rad(Rotation),
                    center: new Vector2(CenterX, CenterY)))
            {
                return;
            }

            RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
            HitTestDeclaration hitTest = DeclareSampling(
                displacementMap,
                map,
                DrawableMapTransformKind.Rotation,
                vector: default,
                MathUtilities.Deg2Rad(Rotation),
                center: new Vector2(CenterX, CenterY),
                spreadMethod,
                channel,
                signed);
            context.Shader(ShaderDescription.WholeSource(
                s_shaderSource,
                RenderBoundsContract.FullInput,
                bindings =>
                {
                    AddDisplacementBindings(bindings, map, channel, signed);
                    bindings.Uniform("uAngle", MathUtilities.Deg2Rad(Rotation));
                    bindings.Uniform(
                        "uPivot",
                        new Vector2(CenterX, CenterY),
                        BindPivot);
                },
                spreadMethod.ToSKShaderTileMode(),
                hitTest: hitTest.Contract,
                hitTestResources: hitTest.Resources,
                slots: hitTest.Slots));
        }
    }
}
