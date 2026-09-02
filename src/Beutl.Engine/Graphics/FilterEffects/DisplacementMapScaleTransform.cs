using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapScaleTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float2 uScale;
        uniform float2 uPivot;
        uniform int uChannel;
        uniform int uSigned;

        float getDisplacement(half4 dispColor) {
            float d;
            if (uChannel == 0) d = dispColor.a;
            else {
                if (uChannel == 1) d = dot(dispColor.rgb, half3(0.2126, 0.7152, 0.0722));
                else if (uChannel == 2) d = dispColor.r;
                else if (uChannel == 3) d = dispColor.g;
                else d = dispColor.b;
                d = d * dispColor.a;
            }
            if (uSigned != 0) d = d * 2.0 - 1.0;
            return d;
        }

        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 s = max(mix(float2(1.0, 1.0), uScale, getDisplacement(dispColor)), float2(0.001, 0.001));

            float2 uv = (coord - uPivot) / s + uPivot;
            return src.eval(uv);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public DisplacementMapScaleTransform()
    {
        ScanProperties<DisplacementMapScaleTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Scale { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.ScaleTransform_ScaleX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> ScaleX { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.ScaleTransform_ScaleY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> ScaleY { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.CenterX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.CenterY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>();

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
                    DrawableMapTransformKind.Scale,
                    new Vector2(
                        Scale * ScaleX / 10000,
                        Scale * ScaleY / 10000),
                    angle: 0,
                    center: new Vector2(CenterX, CenterY)))
            {
                return;
            }

            RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
            HitTestDeclaration hitTest = DeclareSampling(
                displacementMap,
                map,
                DrawableMapTransformKind.Scale,
                new Vector2(
                    Scale * ScaleX / 10000,
                    Scale * ScaleY / 10000),
                angle: 0,
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
                    bindings.Uniform(
                        "uScale",
                        new Vector2(
                            Scale * ScaleX / 10000,
                            Scale * ScaleY / 10000));
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
