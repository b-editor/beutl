using System.Numerics;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform
{
    private protected static bool TryApplyDrawableMap(
        FilterEffectContext context,
        Brush.Resource displacementMap,
        GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel,
        bool signed,
        DrawableMapTransformKind kind,
        Vector2 vector,
        float angle,
        Vector2 center)
    {
        if (ResolveDrawableBrush(displacementMap) is null)
            return false;

        context.CustomEffect(
            new DrawableMapData(
                displacementMap,
                spreadMethod,
                channel,
                signed,
                kind,
                vector,
                angle,
                center),
            ApplyDrawableMap,
            static (_, bounds) => bounds);
        return true;
    }

    private static DrawableBrush.Resource? ResolveDrawableBrush(Brush.Resource? brush)
        => ResolvePresentedBrush(brush) as DrawableBrush.Resource;

    private static void ApplyDrawableMap(
        DrawableMapData data,
        CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget effectTarget = context.Targets[i];
            EffectTarget output = context.CreateTargetLike(effectTarget);
            try
            {
                if (output.RenderTarget is null || output.Scale.IsUnbounded)
                {
                    output.Dispose();
                    continue;
                }

                float density = output.Scale.Value;
                using SKShader displacementMapShaderRaw = DisplacementMapShaderFactory.CreateOrTransparent(
                        context,
                        data.Map,
                        new Rect(effectTarget.Bounds.Size),
                        density);

                Vector semanticOrigin = effectTarget.Bounds.Position - effectTarget.RasterBounds.Position;
                SKMatrix mapMatrix = SKMatrix.CreateScaleTranslation(
                    density,
                    density,
                    (float)semanticOrigin.X * density,
                    (float)semanticOrigin.Y * density);
                using SKShader? mappedDisplacementMap = mapMatrix.IsIdentity
                    ? null
                    : displacementMapShaderRaw.WithLocalMatrix(mapMatrix);
                SKShader displacementMapShader = mappedDisplacementMap ?? displacementMapShaderRaw;

                using SKSLShaderBuilder builder = s_drawableMapShader.Value.CreateBuilder();
                builder.Children["uDisplacementMap"] = displacementMapShader;
                builder.Uniforms["uMode"] = (int)data.Kind;
                builder.Uniforms["uVector"] = data.Kind == DrawableMapTransformKind.Translate
                    ? new SKPoint(data.Vector.X * density, data.Vector.Y * density)
                    : new SKPoint(data.Vector.X, data.Vector.Y);
                builder.Uniforms["uAngle"] = data.Angle;
                builder.Uniforms["uPivot"] = new SKPoint(
                    (float)(semanticOrigin.X + effectTarget.Bounds.Width / 2 + data.Center.X) * density,
                    (float)(semanticOrigin.Y + effectTarget.Bounds.Height / 2 + data.Center.Y) * density);
                builder.Uniforms["uChannel"] = (int)data.Channel;
                builder.Uniforms["uSigned"] = data.Signed ? 1 : 0;

                SKShaderTileMode tileMode = data.SpreadMethod.ToSKShaderTileMode();
                bool rendered = context.UseMappedInputShader(
                    effectTarget,
                    output,
                    (Builder: builder, Shader: s_drawableMapShader.Value, Context: context, Output: output),
                    static (state, mappedSource) =>
                    {
                        state.Builder.Children["src"] = mappedSource;
                        state.Shader.RenderToTarget(state.Context, state.Builder, state.Output);
                    },
                    tileMode,
                    tileMode);
                if (!rendered)
                {
                    output.Dispose();
                    continue;
                }

                effectTarget.Dispose();
                context.Targets[i] = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    private readonly record struct DrawableMapData(
        Brush.Resource Map,
        GradientSpreadMethod SpreadMethod,
        DisplacementMapChannel Channel,
        bool Signed,
        DrawableMapTransformKind Kind,
        Vector2 Vector,
        float Angle,
        Vector2 Center);
}
