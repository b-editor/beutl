using Beutl.Animation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract class DisplacementMapTransform : Animatable, IAffectsRender
{
    protected DisplacementMapTransform()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : DisplacementMapTransform
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                    if (e.NewValue is IAffectsRender newAffectsRender)
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                }
            });
        }
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    internal abstract void ApplyTo(IBrush displacementMap, FilterEffectContext context);
}

public class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    public static readonly CoreProperty<float> XProperty;
    public static readonly CoreProperty<float> YProperty;
    private float _x;
    private float _y;
    private static readonly SKRuntimeEffect s_runtimeEffect;

    static DisplacementMapTranslateTransform()
    {
        XProperty = ConfigureProperty<float, DisplacementMapTranslateTransform>(nameof(X))
            .Accessor(o => o.X, (o, v) => o.X = v)
            .DefaultValue(0)
            .Register();

        YProperty = ConfigureProperty<float, DisplacementMapTranslateTransform>(nameof(Y))
            .Accessor(o => o.Y, (o, v) => o.Y = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<DisplacementMapTranslateTransform>(XProperty, YProperty);

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
        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out var errorText);
    }

    public float X
    {
        get => _x;
        set => SetAndRaise(XProperty, ref _x, value);
    }

    public float Y
    {
        get => _y;
        set => SetAndRaise(YProperty, ref _y, value);
    }

    internal override void ApplyTo(IBrush displacementMap, FilterEffectContext context)
    {
        context.CustomEffect((displacementMap, X, Y),
            (d, c) =>
            {
                var (displacementMap_, x, y) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), displacementMap_, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = SKShader.CreateImage(image);

                    // SKRuntimeShaderBuilderを作成して、child shaderとuniformを設定
                    var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);

                    // child shaderとしてテクスチャ用のシェーダーを設定
                    builder.Children["uBaseTexture"] = baseShader;
                    builder.Children["uDisplacementMap"] = displacementMapShader;

                    builder.Uniforms["uTranslation"] = new SKPoint(x, y);

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

public class DisplacementMapScaleTransform : DisplacementMapTransform
{
    public static readonly CoreProperty<float> ScaleProperty;
    public static readonly CoreProperty<float> ScaleXProperty;
    public static readonly CoreProperty<float> ScaleYProperty;
    public static readonly CoreProperty<float> CenterXProperty;
    public static readonly CoreProperty<float> CenterYProperty;
    private float _scale = 100;
    private float _scaleX = 100;
    private float _scaleY = 100;
    private float _centerX;
    private float _centerY;
    private static readonly SKRuntimeEffect s_runtimeEffect;

    static DisplacementMapScaleTransform()
    {
        ScaleProperty = ConfigureProperty<float, DisplacementMapScaleTransform>(nameof(Scale))
            .Accessor(o => o.Scale, (o, v) => o.Scale = v)
            .DefaultValue(100)
            .Register();

        ScaleXProperty = ConfigureProperty<float, DisplacementMapScaleTransform>(nameof(ScaleX))
            .Accessor(o => o.ScaleX, (o, v) => o.ScaleX = v)
            .DefaultValue(100)
            .Register();

        ScaleYProperty = ConfigureProperty<float, DisplacementMapScaleTransform>(nameof(ScaleY))
            .Accessor(o => o.ScaleY, (o, v) => o.ScaleY = v)
            .DefaultValue(100)
            .Register();

        CenterXProperty = ConfigureProperty<float, DisplacementMapScaleTransform>(nameof(CenterX))
            .Accessor(o => o.CenterX, (o, v) => o.CenterX = v)
            .DefaultValue(0)
            .Register();

        CenterYProperty = ConfigureProperty<float, DisplacementMapScaleTransform>(nameof(CenterY))
            .Accessor(o => o.CenterY, (o, v) => o.CenterY = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<DisplacementMapScaleTransform>(
            ScaleProperty,
            ScaleXProperty,
            ScaleYProperty,
            CenterXProperty,
            CenterYProperty);

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
    }

    public float Scale
    {
        get => _scale;
        set => SetAndRaise(ScaleProperty, ref _scale, value);
    }

    public float ScaleX
    {
        get => _scaleX;
        set => SetAndRaise(ScaleXProperty, ref _scaleX, value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => SetAndRaise(ScaleYProperty, ref _scaleY, value);
    }

    public float CenterX
    {
        get => _centerX;
        set => SetAndRaise(CenterXProperty, ref _centerX, value);
    }

    public float CenterY
    {
        get => _centerY;
        set => SetAndRaise(CenterYProperty, ref _centerY, value);
    }

    internal override void ApplyTo(IBrush displacementMap, FilterEffectContext context)
    {
        context.CustomEffect(
            (displacementMap, x: Scale * ScaleX / 10000, y: Scale * ScaleY / 10000,
                center: new Point(CenterX, CenterY)),
            (d, c) =>
            {
                var (displacementMap_, scaleX, scaleY, center) = d;
                for (int i = 0; i < c.Targets.Count; i++)
                {
                    EffectTarget effectTarget = c.Targets[i];
                    var renderTarget = effectTarget.RenderTarget!;
                    using var displacementMapShader =
                        new BrushConstructor(new(effectTarget.Bounds.Size), displacementMap_, BlendMode.SrcOver)
                            .CreateShader();

                    using var image = renderTarget.Value.Snapshot();
                    using var baseShader = SKShader.CreateImage(image);

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
