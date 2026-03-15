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
    internal abstract void ApplyTo(
        Brush.Resource displacementMap, Resource resource, GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel, bool signed, FilterEffectContext context);
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
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uTranslation"] = new SKPoint(x, y);
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
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uScale"] = new SKPoint(scaleX, scaleY);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        effectTarget.Bounds.Width / 2 + center.X,
                        effectTarget.Bounds.Height / 2 + center.Y);
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
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = image.ToShader(sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = s_shader.CreateBuilder();

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uAngle"] = MathUtilities.Deg2Rad(rotation);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        effectTarget.Bounds.Width / 2 + center.X,
                        effectTarget.Bounds.Height / 2 + center.Y);
                    builder.Uniforms["uChannel"] = (int)ch;
                    builder.Uniforms["uSigned"] = isSigned ? 1 : 0;

                    // 新しいターゲットに適用
                    c.Targets[i] = s_shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
                }
            });
    }
}
