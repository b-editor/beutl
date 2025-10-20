using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform : EngineObject, IAffectsRender
{
    internal abstract void ApplyTo(
        Brush.Resource displacementMap, Resource resource, GradientSpreadMethod spreadMethod, FilterEffectContext context);
}

[Display(Name = nameof(Strings.Translate), ResourceType = typeof(Strings))]
public partial class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapTranslateTransform>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static DisplacementMapTranslateTransform()
    {
        // SKSLコード（child shaderとして uBaseTexture と uDisplacementMap を使用）
        string sksl =
            """
            uniform shader uBaseTexture;
            uniform shader uDisplacementMap;

            uniform float2 uTranslation;
            uniform float2 uPivot;

            half4 main(float2 coord) {
                half4 dispColor = uDisplacementMap.eval(coord);
                float2 offset = uTranslation * dispColor.a;

                float2 uv = coord + offset;
                return uBaseTexture.eval(uv);
            }
            """;

        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public DisplacementMapTranslateTransform()
    {
        ScanProperties<DisplacementMapTranslateTransform>();
    }

    public IProperty<float> X { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> Y { get; } = Property.CreateAnimatable<float>();

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, FilterEffectContext context)
    {
        if (s_runtimeEffect is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect((displacementMap, r, spreadMethod, X, Y),
            (d, c) =>
            {
                var (map, r, sm, x, y) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = SKShader.CreateImage(
                        image, sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uTranslation"] = new SKPoint(r.X, r.Y);

                    // 最終的なシェーダーを生成
                    using (SKShader finalShader = builder.Build())
                    using (var paint = new SKPaint())
                    {
                        var newTarget = c.CreateTarget(effectTarget.Bounds);
                        var canvas = newTarget.RenderTarget!.Value.Canvas;
                        paint.Shader = finalShader;
                        canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                        c.Targets[i] = newTarget;
                    }

                    effectTarget.Dispose();
                }
            });
    }
}

[Display(Name = nameof(Strings.Scale), ResourceType = typeof(Strings))]
public partial class DisplacementMapScaleTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapScaleTransform>();
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static DisplacementMapScaleTransform()
    {
        string sksl =
            """
            uniform shader uBaseTexture;
            uniform shader uDisplacementMap;

            uniform float2 uScale;
            uniform float2 uPivot;

            half4 main(float2 coord) {
                half4 dispColor = uDisplacementMap.eval(coord);
                float2 amount = float2(dispColor.a, dispColor.a);
                float2 s = mix(float2(1.0, 1.0), uScale, dispColor.a);

                float2 uv = (coord - uPivot) / s + uPivot;
                return uBaseTexture.eval(uv);
            }
            """;
        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out var errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public DisplacementMapScaleTransform()
    {
        ScanProperties<DisplacementMapScaleTransform>();
    }

    public IProperty<float> Scale { get; } = Property.CreateAnimatable<float>(100);

    public IProperty<float> ScaleX { get; } = Property.CreateAnimatable<float>(100);

    public IProperty<float> ScaleY { get; } = Property.CreateAnimatable<float>(100);

    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>();

    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>();

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, FilterEffectContext context)
    {
        if (s_runtimeEffect is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect(
            (displacementMap, spreadMethod, x: r.Scale * r.ScaleX / 10000, y: r.Scale * r.ScaleY / 10000,
                center: new Point(r.CenterX, r.CenterY)),
            (d, c) =>
            {
                var (map, sm, scaleX, scaleY, center) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = SKShader.CreateImage(
                        image, sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uScale"] = new SKPoint(scaleX, scaleY);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        effectTarget.Bounds.Width / 2 + center.X,
                        effectTarget.Bounds.Height / 2 + center.Y);

                    // 最終的なシェーダーを生成
                    using (SKShader finalShader = builder.Build())
                    using (var paint = new SKPaint())
                    {
                        var newTarget = c.CreateTarget(effectTarget.Bounds);
                        var canvas = newTarget.RenderTarget!.Value.Canvas;
                        paint.Shader = finalShader;
                        canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                        c.Targets[i] = newTarget;
                    }

                    effectTarget.Dispose();
                }
            });
    }
}

[Display(Name = nameof(Strings.Rotation), ResourceType = typeof(Strings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    private static readonly ILogger s_logger = Log.CreateLogger<DisplacementMapRotationTransform>();
    private static readonly SKRuntimeEffect s_runtimeEffect;

    static DisplacementMapRotationTransform()
    {
        string sksl =
            """
            uniform shader uBaseTexture;
            uniform shader uDisplacementMap;

            uniform float uAngle;
            uniform float2 uPivot;

            half4 main(float2 coord) {
                half4 dispColor = uDisplacementMap.eval(coord);
                float2 offset = float2(cos(uAngle * dispColor.a), sin(uAngle * dispColor.a));

                float2 uv = coord - uPivot;
                float2 rotated = float2(uv.x * offset.x - uv.y * offset.y, uv.x * offset.y + uv.y * offset.x);
                uv = rotated + uPivot;
                return uBaseTexture.eval(uv);
            }
            """;
        // SKRuntimeEffectを使ってSKSLコードをコンパイル
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out var errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
        }
    }

    public DisplacementMapRotationTransform()
    {
        ScanProperties<DisplacementMapRotationTransform>();
    }

    public IProperty<float> Rotation { get; } = Property.CreateAnimatable<float>(0);

    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>(0);

    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>(0);

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, FilterEffectContext context)
    {
        if (s_runtimeEffect is null) throw new InvalidOperationException("Failed to compile SKSL.");
        var r = (Resource)resource;

        context.CustomEffect(
            (displacementMap, spreadMethod, r.Rotation, new Point(r.CenterX, r.CenterY)),
            (d, c) =>
            {
                var (map, sm, rotation, center) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), map, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = SKShader.CreateImage(
                        image, sm.ToSKShaderTileMode(), sm.ToSKShaderTileMode());

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uAngle"] = MathUtilities.Deg2Rad(rotation);
                    builder.Uniforms["uPivot"] = new SKPoint(
                        effectTarget.Bounds.Width / 2 + center.X,
                        effectTarget.Bounds.Height / 2 + center.Y);

                    // 最終的なシェーダーを生成
                    using (SKShader finalShader = builder.Build())
                    using (var paint = new SKPaint())
                    {
                        var newTarget = c.CreateTarget(effectTarget.Bounds);
                        var canvas = newTarget.RenderTarget!.Value.Canvas;
                        paint.Shader = finalShader;
                        canvas.DrawRect(new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height), paint);

                        c.Targets[i] = newTarget;
                    }

                    effectTarget.Dispose();
                }
            });
    }
}
