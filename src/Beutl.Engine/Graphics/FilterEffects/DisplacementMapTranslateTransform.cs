using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.TranslateTransform), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float2 uTranslation;

        """
        + DisplacementSamplingSource
        + """


        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 offset = uTranslation * getDisplacement(dispColor);

            float2 uv = coord + offset;
            return src.eval(uv);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    public DisplacementMapTranslateTransform()
    {
        ScanProperties<DisplacementMapTranslateTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_X), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> X { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_Y), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Y { get; } = Property.CreateAnimatable<float>();

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
                    DrawableMapTransformKind.Translate,
                    new Vector2(X, Y),
                    angle: 0,
                    center: default))
            {
                return;
            }

            RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
            HitTestDeclaration hitTest = DeclareSampling(
                displacementMap,
                map,
                DrawableMapTransformKind.Translate,
                new Vector2(X, Y),
                angle: 0,
                center: default,
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
                        "uTranslation",
                        new Vector2(X, Y),
                        BindScaledVector);
                },
                spreadMethod.ToSKShaderTileMode(),
                hitTest: hitTest.Contract,
                hitTestResources: hitTest.Resources,
                slots: hitTest.Slots));
        }
    }
}
