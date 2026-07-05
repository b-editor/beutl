using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;
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

    internal abstract void ApplyTo(
        Brush.Resource displacementMap, Resource resource, GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel, bool signed, FilterEffectContext context);

    /// <summary>
    /// Appends this transform as a non-invariant whole-source shader node (feature 004, D7). The base texture is the
    /// implicit <c>src</c> child (re-baked into the identity output rect by the executor, tiled per
    /// <paramref name="spreadMethod"/>); the displacement map is an extra child shader built here.
    /// </summary>
    internal abstract void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed);

    // Builds the displacement-map child shader over the input rect at the describe-time working density (cross-sampled
    // at device px, so a w != 1 buffer scales the map's local matrix), owned and disposed by the graph.
    private protected static ChildBinding? BuildMapChild(EffectGraphBuilder builder, Brush.Resource map, float w)
    {
        SKShader? raw = new BrushConstructor(
                new Rect(builder.Bounds.Size), map, BlendMode.SrcOver, w, builder.MaxWorkingScale)
            .CreateShader();
        if (raw is null)
            return null;
        if (w == 1f)
            return builder.Child("uDisplacementMap", raw);

        builder.Track(raw);
        return builder.Child("uDisplacementMap", raw.WithLocalMatrix(SKMatrix.CreateScale(w, w)));
    }
}

[Display(Name = nameof(GraphicsStrings.TranslateTransform), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapTranslateTransform>();
    private static readonly SKSLShader? s_shader;

    static DisplacementMapTranslateTransform()
    {
        // SKSLコード（child shaderとして uBaseTexture と uDisplacementMap を使用）
        string sksl =
            """
            uniform shader uBaseTexture;
            uniform shader uDisplacementMap;

            uniform float2 uTranslation;
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
                float2 offset = uTranslation * getDisplacement(dispColor);

                float2 uv = coord + offset;
                return uBaseTexture.eval(uv);
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public DisplacementMapTranslateTransform()
    {
        ScanProperties<DisplacementMapTranslateTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_X), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> X { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_Y), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Y { get; } = Property.CreateAnimatable<float>();

    private static readonly string s_describeSource =
        "uniform shader src;\n"
        + "uniform shader uDisplacementMap;\n"
        + "uniform float2 uTranslation;\n"
        + "uniform int uChannel;\n"
        + "uniform int uSigned;\n"
        + GetDisplacementFunction + "\n"
        + "half4 main(float2 coord) {\n"
        + "    half4 dispColor = uDisplacementMap.eval(coord);\n"
        + "    float2 offset = uTranslation * getDisplacement(dispColor);\n"
        + "    float2 uv = coord + offset;\n"
        + "    return src.eval(uv);\n"
        + "}";

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        float w = builder.WorkingScale;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap, w);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.Identity,
            u => u.Float2("uTranslation", r.X * w, r.Y * w)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        if (s_shader is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect((displacementMap, r, spreadMethod, channel, signed, r.X, r.Y),
            (d, c) =>
            {
                var (map, r, sm, ch, isSigned, x, y) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    using EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    // Use the clamped density so uniforms / map brush match the buffer.
                    float w = c.ResolveTargetDensity(effectTarget.Bounds);
                    using var displacementMapShaderRaw =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver, w,
                                c.MaxWorkingScale)
                            .CreateShader();
                    // Scale the map's local matrix by w so it cross-samples at device-px coords.
                    using SKShader? displacementMapShaderScaled =
                        w != 1f && displacementMapShaderRaw is { } rawShader
                            ? rawShader.WithLocalMatrix(SKMatrix.CreateScale(w, w))
                            : null;
                    SKShader? displacementMapShader = displacementMapShaderScaled ?? displacementMapShaderRaw;

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    // Absolute-px translation scales by w (shader operates in device px).
                    builder.Uniforms["uTranslation"] = new SKPoint(x * w, y * w);
                    builder.Uniforms["uChannel"] = (int)ch;
                    builder.Uniforms["uSigned"] = isSigned ? 1 : 0;

                    // 新しいターゲットに適用
                    c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
                }
            });
    }
}

[Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapScaleTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapScaleTransform>();
    private static readonly SKSLShader? s_shader;

    static DisplacementMapScaleTransform()
    {
        string sksl =
            """
            uniform shader uBaseTexture;
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
                return uBaseTexture.eval(uv);
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

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

    private static readonly string s_describeSource =
        "uniform shader src;\n"
        + "uniform shader uDisplacementMap;\n"
        + "uniform float2 uScale;\n"
        + "uniform float2 uPivot;\n"
        + "uniform int uChannel;\n"
        + "uniform int uSigned;\n"
        + GetDisplacementFunction + "\n"
        + "half4 main(float2 coord) {\n"
        + "    half4 dispColor = uDisplacementMap.eval(coord);\n"
        + "    float2 s = max(mix(float2(1.0, 1.0), uScale, getDisplacement(dispColor)), float2(0.001, 0.001));\n"
        + "    float2 uv = (coord - uPivot) / s + uPivot;\n"
        + "    return src.eval(uv);\n"
        + "}";

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        float w = builder.WorkingScale;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap, w);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.Identity,
            u => u.Float2("uScale", r.Scale * r.ScaleX / 10000, r.Scale * r.ScaleY / 10000)
                  .Float2("uPivot", (builder.Bounds.Width / 2 + r.CenterX) * w, (builder.Bounds.Height / 2 + r.CenterY) * w)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        if (s_shader is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect(
            (displacementMap, spreadMethod, channel, signed, x: r.Scale * r.ScaleX / 10000, y: r.Scale * r.ScaleY / 10000,
                center: new Point(r.CenterX, r.CenterY)),
            (d, c) =>
            {
                var (map, sm, ch, isSigned, scaleX, scaleY, center) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    using var effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    // Use the clamped density so uniforms / map brush match the buffer.
                    float w = c.ResolveTargetDensity(effectTarget.Bounds);
                    using var displacementMapShaderRaw =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver, w,
                                c.MaxWorkingScale)
                            .CreateShader();
                    // Scale the map's local matrix by w so it cross-samples at device-px coords.
                    using SKShader? displacementMapShaderScaled =
                        w != 1f && displacementMapShaderRaw is { } rawShader
                            ? rawShader.WithLocalMatrix(SKMatrix.CreateScale(w, w))
                            : null;
                    SKShader? displacementMapShader = displacementMapShaderScaled ?? displacementMapShaderRaw;

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    // uScale is density-independent; the pivot maps logical-px to device-px, so it scales by w.
                    builder.Uniforms["uScale"] = new SKPoint(scaleX, scaleY);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        (effectTarget.Bounds.Width / 2 + center.X) * w,
                        (effectTarget.Bounds.Height / 2 + center.Y) * w);
                    builder.Uniforms["uChannel"] = (int)ch;
                    builder.Uniforms["uSigned"] = isSigned ? 1 : 0;

                    // 新しいターゲットに適用
                    c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
                }
            });
    }
}

[Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapRotationTransform>();
    private static readonly SKSLShader? s_shader;

    static DisplacementMapRotationTransform()
    {
        string sksl =
            """
            uniform shader uBaseTexture;
            uniform shader uDisplacementMap;

            uniform float uAngle;
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
                float disp = getDisplacement(dispColor);
                float2 offset = float2(cos(uAngle * disp), sin(uAngle * disp));

                float2 uv = coord - uPivot;
                float2 rotated = float2(uv.x * offset.x - uv.y * offset.y, uv.x * offset.y + uv.y * offset.x);
                uv = rotated + uPivot;
                return uBaseTexture.eval(uv);
            }
            """;

        if (!SKSLShader.TryCreate(sksl, out s_shader, out string? errorText))
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

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

    private static readonly string s_describeSource =
        "uniform shader src;\n"
        + "uniform shader uDisplacementMap;\n"
        + "uniform float uAngle;\n"
        + "uniform float2 uPivot;\n"
        + "uniform int uChannel;\n"
        + "uniform int uSigned;\n"
        + GetDisplacementFunction + "\n"
        + "half4 main(float2 coord) {\n"
        + "    half4 dispColor = uDisplacementMap.eval(coord);\n"
        + "    float disp = getDisplacement(dispColor);\n"
        + "    float2 offset = float2(cos(uAngle * disp), sin(uAngle * disp));\n"
        + "    float2 uv = coord - uPivot;\n"
        + "    float2 rotated = float2(uv.x * offset.x - uv.y * offset.y, uv.x * offset.y + uv.y * offset.x);\n"
        + "    uv = rotated + uPivot;\n"
        + "    return src.eval(uv);\n"
        + "}";

    internal override void Describe(
        EffectGraphBuilder builder, Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed)
    {
        var r = (Resource)resource;
        float w = builder.WorkingScale;
        ChildBinding? mapChild = BuildMapChild(builder, displacementMap, w);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.Identity,
            u => u.Float("uAngle", MathUtilities.Deg2Rad(r.Rotation))
                  .Float2("uPivot", (builder.Bounds.Width / 2 + r.CenterX) * w, (builder.Bounds.Height / 2 + r.CenterY) * w)
                  .Int("uChannel", (int)channel)
                  .Int("uSigned", signed ? 1 : 0),
            children: [mapChild],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        if (s_shader is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect(
            (displacementMap, spreadMethod, channel, signed, r.Rotation, new Point(r.CenterX, r.CenterY)),
            (d, c) =>
            {
                var (map, sm, ch, isSigned, rotation, center) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    using var effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    // Use the clamped density so uniforms / map brush match the buffer.
                    float w = c.ResolveTargetDensity(effectTarget.Bounds);
                    using var displacementMapShaderRaw =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver, w,
                                c.MaxWorkingScale)
                            .CreateShader();
                    // Scale the map's local matrix by w so it cross-samples at device-px coords.
                    using SKShader? displacementMapShaderScaled =
                        w != 1f && displacementMapShaderRaw is { } rawShader
                            ? rawShader.WithLocalMatrix(SKMatrix.CreateScale(w, w))
                            : null;
                    SKShader? displacementMapShader = displacementMapShaderScaled ?? displacementMapShaderRaw;

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    // Pivot maps logical-px to device-px (scales by w); the angle is density-independent.
                    builder.Uniforms["uAngle"] = MathUtilities.Deg2Rad(rotation);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        (effectTarget.Bounds.Width / 2 + center.X) * w,
                        (effectTarget.Bounds.Height / 2 + center.Y) * w);
                    builder.Uniforms["uChannel"] = (int)ch;
                    builder.Uniforms["uSigned"] = isSigned ? 1 : 0;

                    // 新しいターゲットに適用
                    c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
                }
            });
    }
}
