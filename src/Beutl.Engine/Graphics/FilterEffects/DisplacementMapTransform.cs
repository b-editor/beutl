using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform : EngineObject
{
    /// <summary>
    /// The displacement sample every transform reads: the uniforms that name the source channel and its
    /// signedness, and the function that resolves one sample through them.
    /// </summary>
    private protected const string DisplacementSamplingSource =
        """
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
            }
            if (uSigned != 0) d = d * 2.0 - 1.0;
            return d;
        }
        """;

    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform int uMode;
        uniform float2 uVector;
        uniform float uAngle;
        uniform float2 uPivot;

        """
        + DisplacementSamplingSource
        + """


        half4 main(float2 coord) {
            float disp = getDisplacement(uDisplacementMap.eval(coord));
            if (uMode == 0) {
                return src.eval(coord + uVector * disp);
            }
            if (uMode == 1) {
                float2 scale = max(
                    mix(float2(1.0, 1.0), uVector, disp),
                    float2(0.001, 0.001));
                return src.eval((coord - uPivot) / scale + uPivot);
            }

            float2 rotation = float2(cos(uAngle * disp), sin(uAngle * disp));
            float2 uv = coord - uPivot;
            uv = float2(
                uv.x * rotation.x - uv.y * rotation.y,
                uv.x * rotation.y + uv.y * rotation.x);
            return src.eval(uv + uPivot);
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.WholeSource);

    private static readonly Lazy<SKSLShader> s_drawableMapShader =
        new(() => SKSLShader.Create(ShaderSource));

    // SkiaSharp declares its named colours as plain static fields, which anything in the process can
    // assign; a binder read by a keyed recording callback needs a value nothing can write.
    private static readonly SKColor s_transparent = SKColors.Transparent;

    // The working format of every stage in the graph. A map sampled in any other space would report a
    // different displacement from the one the entry point read.
    private static readonly SKColorSpace s_workingColorSpace = SKColorSpace.CreateSrgbLinear();

    public partial class Resource
    {
        /// <summary>
        /// Lowers this transform into <paramref name="context"/> for the displacement map the owning
        /// <see cref="DisplacementMapEffect"/> resolved.
        /// </summary>
        /// <param name="displacementMap">The brush whose pixels supply the displacement.</param>
        /// <param name="spreadMethod">How the input is sampled outside its bounds.</param>
        /// <param name="channel">The channel of <paramref name="displacementMap"/> that carries the displacement.</param>
        /// <param name="signed">Whether the channel is centred on zero rather than starting from it.</param>
        /// <param name="context">The effect context that receives the recorded stages.</param>
        /// <remarks>
        /// <see cref="DisplacementMapEffect"/> calls this once per application in place of its own lowering
        /// whenever <see cref="DisplacementMapEffect.Transform"/> is set and the map is not being shown. An
        /// out-of-tree transform records its stages through the public <see cref="FilterEffectContext"/> surface
        /// only, because the sampling helpers the built-in transforms share are not part of the contract.
        /// </remarks>
        public abstract void ApplyTo(
            Brush.Resource displacementMap, GradientSpreadMethod spreadMethod,
            DisplacementMapChannel channel, bool signed, FilterEffectContext context);
    }

    /// <summary>Lowers a built-in transform of the given <paramref name="kind"/> into <paramref name="context"/>.</summary>
    private protected static void ApplyCore(
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
        if (TryApplyDrawableMap(
                context,
                displacementMap,
                spreadMethod,
                channel,
                signed,
                kind,
                vector,
                angle,
                center))
        {
            return;
        }

        RenderResource<Brush.Resource> map = context.Borrow(displacementMap);
        context.Shader(ShaderDescription.WholeSource(
            s_shaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                AddDisplacementBindings(bindings, map, channel, signed);
                bindings.Uniform("uMode", (int)kind);
                if (kind == DrawableMapTransformKind.Translate)
                    bindings.Uniform("uVector", vector, BindScaledVector);
                else
                    bindings.Uniform("uVector", vector);
                bindings.Uniform("uAngle", angle);
                bindings.Uniform("uPivot", center, BindPivot);
            },
            spreadMethod.ToSKShaderTileMode()));
    }

    private protected static void AddDisplacementBindings(
        ShaderBindingBuilder bindings,
        RenderResource<Brush.Resource> displacementMap,
        DisplacementMapChannel channel,
        bool signed)
    {
        bindings.Resource(
            "uDisplacementMap",
            displacementMap,
            ShaderResourceCoordinateSpace.OutputDevice,
            CreateDisplacementMapShader);
        bindings.Uniform("uChannel", (int)channel);
        bindings.Uniform("uSigned", signed ? 1 : 0);
    }

    private protected static void BindScaledVector(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
        => writer.Set(value * context.WorkingScale);

    private protected static void BindPivot(
        ShaderUniformWriter writer,
        Vector2 center,
        ShaderExecutionContext context)
    {
        var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
        writer.Set(new Vector2(
            (semanticOrigin.X + context.OutputBounds.Width / 2 + center.X) * context.WorkingScale,
            (semanticOrigin.Y + context.OutputBounds.Height / 2 + center.Y) * context.WorkingScale));
    }

    private static Brush.Resource? ResolvePresentedBrush(Brush.Resource? brush)
        => BrushPresenter.Resource.ResolveTarget(
            brush,
            "A BrushPresenter cycle was detected while lowering a displacement map.");

    private static void CreateDisplacementMapShader(
        ShaderResourceWriter writer,
        Brush.Resource displacementMap,
        ShaderExecutionContext context)
    {
        SKShader? shader = new BrushConstructor(
                new Rect(context.OutputBounds.Size),
                displacementMap,
                BlendMode.SrcOver,
                context.Intent,
                // A drawable map never reaches this binder: TryApplyDrawableMap routes it to the
                // custom-effect path, whose canvas carries the request's materializer.
                drawableBrushMaterializer: null,
                context.WorkingScale,
                context.MaxWorkingScale)
            .CreateShader();
        if (shader is null)
        {
            writer.Set(SKShader.CreateColor(s_transparent));
            return;
        }

        SKShader? mapped = null;
        try
        {
            var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
            SKMatrix localMatrix = SKMatrix.CreateScaleTranslation(
                context.WorkingScale,
                context.WorkingScale,
                semanticOrigin.X * context.WorkingScale,
                semanticOrigin.Y * context.WorkingScale);
            if (localMatrix.IsIdentity)
            {
                writer.Set(shader);
                shader = null;
                return;
            }

            mapped = shader.WithLocalMatrix(localMatrix);
            if (mapped is null)
            {
                writer.Set(shader);
                shader = null;
            }
            else
            {
                writer.Set(mapped);
                mapped = null;
            }
        }
        finally
        {
            mapped?.Dispose();
            shader?.Dispose();
        }
    }

    private protected enum DrawableMapTransformKind : byte
    {
        Translate,
        Scale,
        Rotation,
    }
}
