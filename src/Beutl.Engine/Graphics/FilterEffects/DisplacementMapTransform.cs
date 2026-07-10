using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform : EngineObject
{
    // The displacement function is identical across the translate/scale/rotation whole-source shaders; only the
    // per-pixel remap differs. Kept as one snippet so the three describe sources stay in sync.
    private protected const string GetDisplacementFunction =
        """
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
        """;

    /// <summary>
    /// Appends this transform as a non-invariant whole-source shader node (feature 004, D7). The base texture is the
    /// implicit <c>src</c> child (re-baked into the identity output rect by the executor, tiled per
    /// <paramref name="spreadMethod"/>); the displacement map is an extra child shader built here.
    /// </summary>
    internal abstract void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed);

    // Binds the displacement-map child as a DEFERRED child shader (A4): it is cross-sampled in device space with no
    // canvas density transform, so its local matrix must track the pass's EXECUTION working scale, not the describe-time
    // one — the resource-resolution re-clamp (execution-plan §C3.2) can run the pass below its describe density, and a
    // describe-time bake would then mis-scale the map lookup. The factory rebuilds the map over the input rect at the
    // execution density (threading the pass's diagnostics so a DrawableBrush map's nested render stays observable,
    // FR-017) and the executor disposes each per-pass product. Returns null (identity, no node) only when the brush
    // kind produces no shader at all — decided by the non-rendering BrushConstructor.CanCreateShader predicate so
    // Describe does NO rendering, target allocation, or GPU work (contract A1 / FR-001); the removed describe-time
    // probe used to render a DrawableBrush map here. The map (and the scale/rotation pivot) is anchored in the
    // FULL-frame device space, so every transform declares a RenderTime bounds contract: an ROI crop by a downstream
    // deflating pass would shift the map/pivot off the baked buffer (M3).
    private protected static ChildBinding? BuildMapChild(EffectGraphBuilder builder, Brush.Resource map)
    {
        Rect mapBounds = new(builder.Bounds.Size);
        float maxWorkingScale = builder.MaxWorkingScale;

        if (!BrushConstructor.CanCreateShader(map))
            return null;

        return ChildBinding.Deferred("uDisplacementMap", context =>
        {
            float w = context.WorkingScale;
            SKShader shader =
                new BrushConstructor(mapBounds, map, BlendMode.SrcOver, w, maxWorkingScale, context.Diagnostics)
                    .CreateShader()
                ?? SKShader.CreateColor(SKColors.Transparent);
            if (w == 1f)
                return shader;

            // WithLocalMatrix returns a new shader holding a native ref to `shader`, so disposing the base here is
            // leak-free (it survives inside `scaled`) and leaves the executor one shader to own.
            SKShader scaled = shader.WithLocalMatrix(SKMatrix.CreateScale(w, w));
            shader.Dispose();
            return scaled;
        });
    }
}

[Display(Name = nameof(GraphicsStrings.TranslateTransform), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    private static readonly string s_describeSource =
        $$"""
        uniform shader src;
        uniform shader uDisplacementMap;
        uniform float2 uTranslation;
        uniform int uChannel;
        uniform int uSigned;
        {{GetDisplacementFunction}}
        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 offset = uTranslation * getDisplacement(dispColor);
            float2 uv = coord + offset;
            return src.eval(uv);
        }
        """;

    public DisplacementMapTranslateTransform()
    {
        ScanProperties<DisplacementMapTranslateTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_X), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> X { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_Y), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Y { get; } = Property.CreateAnimatable<float>();

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.RenderTime,
            u => u.DensityScaledFloat2("uTranslation", r.X, r.Y)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }
}

[Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapScaleTransform : DisplacementMapTransform
{
    private static readonly string s_describeSource =
        $$"""
        uniform shader src;
        uniform shader uDisplacementMap;
        uniform float2 uScale;
        uniform float2 uPivot;
        uniform int uChannel;
        uniform int uSigned;
        {{GetDisplacementFunction}}
        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 s = max(mix(float2(1.0, 1.0), uScale, getDisplacement(dispColor)), float2(0.001, 0.001));
            float2 uv = (coord - uPivot) / s + uPivot;
            return src.eval(uv);
        }
        """;

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

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.RenderTime,
            u => u.Float2("uScale", r.Scale * r.ScaleX / 10000, r.Scale * r.ScaleY / 10000)
                  .DensityScaledFloat2("uPivot", builder.Bounds.Width / 2 + r.CenterX, builder.Bounds.Height / 2 + r.CenterY)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }
}

[Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    private static readonly string s_describeSource =
        $$"""
        uniform shader src;
        uniform shader uDisplacementMap;
        uniform float uAngle;
        uniform float2 uPivot;
        uniform int uChannel;
        uniform int uSigned;
        {{GetDisplacementFunction}}
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

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.RenderTime,
            u => u.Float("uAngle", MathUtilities.Deg2Rad(r.Rotation))
                  .DensityScaledFloat2("uPivot", builder.Bounds.Width / 2 + r.CenterX, builder.Bounds.Height / 2 + r.CenterY)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }
}
