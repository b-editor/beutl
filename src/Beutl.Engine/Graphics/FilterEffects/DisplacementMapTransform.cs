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
    /// <remarks>
    /// Concatenated into each transform's program rather than declared once, because every SKSL program is
    /// compiled on its own and shares no declarations with the next.
    /// </remarks>
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

    private const string DrawableMapShaderSource =
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

    private static readonly Lazy<SKSLShader> s_drawableMapShader =
        new(() => SKSLShader.Create(DrawableMapShaderSource));

    // SkiaSharp declares its named colours as plain static fields, which anything in the process can
    // assign; a binder read by a keyed recording callback needs a value nothing can write.
    private static readonly SKColor s_transparent = SKColors.Transparent;

    private static readonly RenderResourceSlot<Brush.Resource> s_hitTestMapSlot = new();
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
        /// out-of-tree transform records its stages through the public <see cref="FilterEffectContext"/>
        /// surface, typically <see cref="FilterEffectContext.Shader(Shaders.ShaderDescription)"/> with a
        /// <see cref="Shaders.ShaderDescription"/> whose <see cref="Shaders.ShaderBindingBuilder"/> callback
        /// binds <paramref name="displacementMap"/> as a resource. The sampling helpers the built-in
        /// transforms share are not part of the public contract.
        /// </remarks>
        public abstract void ApplyTo(
            Brush.Resource displacementMap, GradientSpreadMethod spreadMethod,
            DisplacementMapChannel channel, bool signed, FilterEffectContext context);
    }

    private protected static RenderResource<Brush.Resource> BorrowDisplacementMap(
        FilterEffectContext context,
        Brush.Resource displacementMap)
        => context.Borrow(displacementMap);

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
    {
        var seen = new HashSet<Brush.Resource>(ReferenceEqualityComparer.Instance);
        while (brush is BrushPresenter.Resource presenter)
        {
            if (!seen.Add(brush))
            {
                throw new InvalidOperationException(
                    "A BrushPresenter cycle was detected while lowering a displacement map.");
            }

            if (presenter.Target is not { } target)
                return null;
            brush = target;
        }

        return brush;
    }

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
                // A drawable map never reaches this binder: TryApplyEffectItemDrawableMap routes it to the
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
