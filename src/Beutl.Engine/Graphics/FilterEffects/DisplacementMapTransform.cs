using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;
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
                d = d * dispColor.a;
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
    private static readonly RenderResourceSlot[] s_hitTestSlots = [s_hitTestMapSlot];

    // The working format of every stage in the graph. A map sampled in any other space would report a
    // different displacement from the one the entry point read.
    private static readonly SKColorSpace s_workingColorSpace = SKColorSpace.CreateSrgbLinear();

    public partial class Resource
    {
        internal abstract void ApplyTo(
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

    /// <summary>The three parts a stage passes to <see cref="ShaderDescription"/> to declare a hit test.</summary>
    private protected readonly record struct HitTestDeclaration(
        RenderHitTestContract? Contract,
        IReadOnlyList<RenderResourceBinding>? Resources,
        IEnumerable<RenderResourceSlot>? Slots);

    /// <summary>Declares a hit test that resolves a coordinate the way the shader fallback resamples one.</summary>
    /// <remarks>
    /// <para>
    /// Every fallback entry point discards the fragment it writes and returns <c>src</c> somewhere else, at a
    /// displacement it reads from the map at that fragment. Forwarding the query unchanged therefore answers
    /// for the input's coverage at the vacated point rather than at the point the stage read: an opaque map
    /// moves content out of a pixel while the query still reports the content that used to be there, and
    /// misses the pixel the content arrived in.
    /// </para>
    /// <para>
    /// Resolving that coordinate needs the displacement itself, which is a sample and not coverage, so the map
    /// is evaluated here through the very <see cref="SKShader"/> the stage binds. A tile brush is the one map
    /// this cannot do: its shader rasterizes an intermediate render target, which a hit test must not
    /// allocate. Those stages keep the forwarded query and the defect it carries.
    /// </para>
    /// </remarks>
    private protected static HitTestDeclaration DeclareSampling(
        Brush.Resource displacementMap,
        RenderResource<Brush.Resource> map,
        DrawableMapTransformKind kind,
        Vector2 vector,
        float angle,
        Vector2 center,
        GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel,
        bool signed)
    {
        if (ResolvePresentedBrush(displacementMap) is TileBrush.Resource)
            return default;

        RenderHitTestContract contract = RenderHitTestContract.Custom(
            new DisplacementSampling(
                kind,
                vector,
                angle,
                center,
                spreadMethod.ToSKShaderTileMode(),
                channel,
                signed),
            static (state, context, point) => state.HitTest(context, point));
        return new HitTestDeclaration(contract, [s_hitTestMapSlot.Bind(map)], s_hitTestSlots);
    }

    /// <summary>Answers a hit test the way a fallback entry point resolves the coordinate it samples.</summary>
    private readonly record struct DisplacementSampling(
        DrawableMapTransformKind Kind,
        Vector2 Vector,
        float Angle,
        Vector2 Center,
        SKShaderTileMode SourceTileMode,
        DisplacementMapChannel Channel,
        bool Signed)
    {
        public bool HitTest(RenderHitTestContext context, Point point)
        {
            Rect outputBounds = context.OutputBounds;

            // The mapping names a source for any coordinate, but the entry point runs only for fragments of
            // this stage's own output. Outside that rectangle the stage evaluated nothing, and
            // RenderHitTestContract.Custom applies no such gate of its own.
            if (!outputBounds.ContainsExclusive(point))
                return false;

            DisplacementMapChannel channel = Channel;
            bool signed = Signed;
            float displacement = context.UseResource(
                s_hitTestMapSlot,
                map => ResolveDisplacement(map, outputBounds, point, channel, signed));
            Point source = ResolveSource(point, outputBounds, displacement);

            IReadOnlyList<RenderHitTestInput> inputs = context.Inputs;
            for (int index = 0; index < inputs.Count; index++)
            {
                RenderHitTestInput input = inputs[index];
                if (TryTileIntoBounds(source, input.Bounds, SourceTileMode, out Point tiled) && input.HitTest(tiled))
                    return true;
            }

            return false;
        }

        /// <remarks>
        /// The uniform binders express the same quantities in the shader's device coordinates. A hit test is
        /// asked in logical coordinates and must answer the same at every scale, so the translation is taken
        /// unscaled and the pivot is rebuilt from the output rectangle rather than from the device grid.
        /// </remarks>
        private Point ResolveSource(Point point, Rect outputBounds, float displacement)
        {
            if (Kind == DrawableMapTransformKind.Translate)
                return point + new Vector(Vector.X * displacement, Vector.Y * displacement);

            Point pivot = outputBounds.Center + new Vector(Center.X, Center.Y);
            float x = point.X - pivot.X;
            float y = point.Y - pivot.Y;
            if (Kind == DrawableMapTransformKind.Scale)
            {
                float scaleX = MathF.Max(float.Lerp(1f, Vector.X, displacement), 0.001f);
                float scaleY = MathF.Max(float.Lerp(1f, Vector.Y, displacement), 0.001f);
                return new Point((x / scaleX) + pivot.X, (y / scaleY) + pivot.Y);
            }

            float theta = Angle * displacement;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);
            return new Point(((x * cos) - (y * sin)) + pivot.X, ((x * sin) + (y * cos)) + pivot.Y);
        }
    }

    /// <summary>Reads the displacement the entry point reads at <paramref name="point"/>.</summary>
    /// <remarks>
    /// A map that produces no shader is the transparent one <see cref="CreateDisplacementMapShader"/> installs
    /// in its place, which displaces by nothing at all - or, signed, by the whole negative extent.
    /// </remarks>
    private static float ResolveDisplacement(
        Brush.Resource map,
        Rect outputBounds,
        Point point,
        DisplacementMapChannel channel,
        bool signed)
    {
        Vector4 color = SampleMap(map, outputBounds, point);
        float value = channel switch
        {
            DisplacementMapChannel.Alpha => color.W,
            // The entry point weights the premultiplied colour and then multiplies by alpha again; this
            // restates that rather than correcting it.
            DisplacementMapChannel.Luminance =>
                ((0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z)) * color.W,
            DisplacementMapChannel.Red => color.X * color.W,
            DisplacementMapChannel.Green => color.Y * color.W,
            _ => color.Z * color.W,
        };

        return signed ? (value * 2f) - 1f : value;
    }

    /// <summary>
    /// Evaluates the map's own shader at one logical point, in the premultiplied working colour space the
    /// entry point receives it in.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateDisplacementMapShader"/> lays the brush out over a rectangle the size of the output
    /// and offsets it onto the output's position, so a logical point addresses the brush at its distance from
    /// that position. The render intent reaches only the tile-brush path, which
    /// <see cref="DeclareSampling"/> has already excluded.
    /// </remarks>
    private static Vector4 SampleMap(Brush.Resource map, Rect outputBounds, Point point)
    {
        using SKShader? shader = new BrushConstructor(
                new Rect(outputBounds.Size),
                map,
                BlendMode.SrcOver,
                RenderIntent.Preview,
                drawableBrushMaterializer: null)
            .CreateShader();
        if (shader is null)
            return default;

        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Premul, s_workingColorSpace);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src, IsAntialias = false };
        float x = point.X - outputBounds.X;
        float y = point.Y - outputBounds.Y;

        // The single pixel's centre, not its corner, is where the shader is read.
        canvas.Translate(0.5f - x, 0.5f - y);
        canvas.DrawRect(new SKRect(x - 0.5f, y - 0.5f, x + 0.5f, y + 0.5f), paint);

        ReadOnlySpan<Half> pixel = MemoryMarshal.Cast<byte, Half>(bitmap.GetPixelSpan());
        return new Vector4((float)pixel[0], (float)pixel[1], (float)pixel[2], (float)pixel[3]);
    }

    /// <summary>Moves a source coordinate onto the input the way the sampler's tile mode does.</summary>
    /// <remarks>
    /// <para>
    /// The stage samples its input with the user's spread method, so a coordinate outside the input reads a
    /// clamped, repeated or mirrored part of it rather than transparency, and the pixel is painted with
    /// whatever that part carries. Only <see cref="SKShaderTileMode.Decal"/> leaves it empty.
    /// </para>
    /// <para>
    /// Where the input's edge is follows the engine's rule for what a content node covers, which is
    /// bottom-right exclusive (<see cref="Rect.ContainsExclusive"/>). Landing on
    /// <see cref="Rect.Right"/> itself would therefore land on the one coordinate the input rejects, so every
    /// mode ends on the last <see langword="float"/> below it. An input with no such coordinate on an axis
    /// covers no column there and carries nothing to read.
    /// </para>
    /// </remarks>
    private static bool TryTileIntoBounds(Point point, Rect bounds, SKShaderTileMode mode, out Point tiled)
    {
        tiled = default;
        float maxX = float.BitDecrement(bounds.Right);
        float maxY = float.BitDecrement(bounds.Bottom);
        if (maxX < bounds.Left || maxY < bounds.Top)
            return false;

        if (mode == SKShaderTileMode.Decal
            && !bounds.ContainsExclusive(point))
        {
            return false;
        }

        tiled = new Point(
            TileCoordinate(point.X, bounds.Left, bounds.Width, maxX, mode),
            TileCoordinate(point.Y, bounds.Top, bounds.Height, maxY, mode));
        return true;
    }

    private static float TileCoordinate(float value, float origin, float extent, float max, SKShaderTileMode mode)
    {
        float offset = value - origin;
        if (mode is SKShaderTileMode.Repeat or SKShaderTileMode.Mirror && extent > 0)
        {
            float period = mode == SKShaderTileMode.Mirror ? extent * 2f : extent;
            offset -= period * MathF.Floor(offset / period);
            if (mode == SKShaderTileMode.Mirror && offset > extent)
                offset = period - offset;
        }

        return Math.Clamp(origin + offset, origin, max);
    }

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
