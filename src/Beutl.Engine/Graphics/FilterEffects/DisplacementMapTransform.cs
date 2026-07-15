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

    private static bool s_forceMapShaderNullForTests;
    private static Exception? s_mapPresenceBindFailureForTests;
    private static SKShader? s_mapShaderBuiltForTests;

    // Test seam: forces the render-time map-shader resolution to null (as a preview allocation failure would) so the
    // absent-map fallback can be exercised without an unallocatable brush. The caller restores the flag afterward.
    internal static void ForceMapShaderNullForTests() => s_forceMapShaderNullForTests = true;

    internal static void ResetMapShaderForTests() => s_forceMapShaderNullForTests = false;

    internal static void ForceMapPresenceBindFailureForTests(Exception exception)
    {
        s_mapShaderBuiltForTests?.Dispose();
        s_mapShaderBuiltForTests = null;
        s_mapPresenceBindFailureForTests = exception;
    }

    internal static SKShader? MapShaderBuiltForTests => s_mapShaderBuiltForTests;

    internal static void ResetMapPresenceBindFailureForTests()
    {
        s_mapPresenceBindFailureForTests = null;
        s_mapShaderBuiltForTests?.Dispose();
        s_mapShaderBuiltForTests = null;
    }

    // Binds the displacement-map child as a DEFERRED child shader (A4): it is cross-sampled in device space with no
    // canvas density transform, so its local matrix must track the pass's EXECUTION working scale, not the describe-time
    // one — the resource-resolution re-clamp (execution-plan §C3.2) can run the pass below its describe density, and a
    // describe-time bake would then mis-scale the map lookup. The factory rebuilds the map over the input rect at the
    // execution density (threading the pass's diagnostics so a DrawableBrush map's nested render stays observable,
    // FR-017) and the executor disposes each per-pass product. Returns null (identity, no node) only when the brush
    // kind produces no shader at all — decided by the non-rendering BrushConstructor.CanCreateShader predicate so
    // Describe does NO rendering, target allocation, or GPU work (contract A1 / FR-001); the removed describe-time
    // probe used to render a DrawableBrush map here. The map (and the scale/rotation pivot) is anchored to the
    // executed target's local device space, so every transform declares a FullFrame bounds contract: an ROI crop by
    // a downstream deflating pass would otherwise change that target and re-anchor the map/pivot. Fan-out still
    // executes one full branch at a time; MapChild uses PassUniformContext.TargetBounds so each branch preserves the
    // legacy per-effect-target anchoring.
    private protected static MapChild? BuildMapChild(EffectGraphBuilder builder, Brush.Resource map)
    {
        if (!BrushConstructor.CanCreateShader(map))
            return null;

        return new MapChild(map, builder.MaxWorkingScale);
    }

    // The per-pass displacement-map child. The deferred child shader and the uMapPresent uniform must agree within a
    // pass, so they share a single resolution: the presence uniform (bound one stage-slot before the child in the same
    // BuildRuntimeRun) builds the fresh shader and records its presence, and the paired child resolve consumes it. The
    // resolution is per pass EXECUTION, not cached across passes — a SplitEffect/PartsSplitEffect fan-out runs this same
    // holder once per branch, and the executor disposes each per-pass product after that branch's draw, so a cached
    // instance would be a use-after-dispose (and double-dispose) for every branch past the first. CanCreateShader gates
    // node creation at describe time, but the map's render-time resolution can still yield null (a preview buffer
    // allocation failure); a transparent fallback then reads as getDisplacement == 0, which the signed remap turns into
    // a full negative displacement rather than identity. So an absent shader is reported through uMapPresent == 0, which
    // makes the shader pass the source through unchanged (exact identity) regardless of Signed/channel.
    private protected sealed class MapChild(Brush.Resource map, float maxWorkingScale)
    {
        private bool _pending;
        private bool _present;
        private SKShader? _shader;

        public ChildBinding Binding => ChildBinding.Deferred("uDisplacementMap", context => Take(context));

        public UniformBindingBuilder BindPresence(UniformBindingBuilder uniforms)
            => uniforms.Deferred("uMapPresent", (builder, name, context) =>
            {
                try
                {
                    Build(context);
                    if (s_mapPresenceBindFailureForTests is { } injected)
                    {
                        s_mapPresenceBindFailureForTests = null;
                        s_mapShaderBuiltForTests = _shader;
                        throw injected;
                    }

                    builder.Uniforms[name] = _present ? 1 : 0;
                }
                catch
                {
                    DisposePendingShader();
                    throw;
                }
            });

        // Builds this pass's fresh displacement-map shader and records its presence for the paired uMapPresent uniform.
        private void Build(in PassUniformContext context)
        {
            DisposePendingShader();
            float w = context.WorkingScale;
            SKShader? shader = null;
            try
            {
                SKShader? created = s_forceMapShaderNullForTests
                    ? null
                    : new BrushConstructor(
                        new Rect(context.TargetBounds.Size), map, BlendMode.SrcOver, context.RenderIntent, w,
                        maxWorkingScale, context.Diagnostics, context.PullPurpose).CreateShader();
                _present = created is not null;

                shader = created ?? SKShader.CreateColor(SKColors.Transparent);
                if (w != 1f)
                {
                    // WithLocalMatrix returns a new shader holding a native ref to `shader`, so disposing the base here
                    // is leak-free (it survives inside `scaled`) and leaves the executor one shader to own.
                    SKShader scaled = shader.WithLocalMatrix(SKMatrix.CreateScale(w, w));
                    shader.Dispose();
                    shader = scaled;
                }

                _shader = shader;
                shader = null;
                _pending = true;
            }
            finally
            {
                // If creation or local-matrix wrapping fails before ownership reaches the paired child binding, the
                // partially built shader has no graph/executor owner and must be released here.
                shader?.Dispose();
            }
        }

        // Hands the executor the shader built by the paired presence resolve and CLEARS the holder so the next fan-out
        // branch rebuilds a fresh one (the executor disposes each per-pass product after its draw). If no presence
        // resolve preceded this call, build now so the child is never left unresolved.
        private SKShader Take(in PassUniformContext context)
        {
            if (!_pending)
                Build(context);

            SKShader shader = _shader!;
            _shader = null;
            _pending = false;
            return shader;
        }

        private void DisposePendingShader()
        {
            _shader?.Dispose();
            _shader = null;
            _pending = false;
            _present = false;
        }
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
        uniform int uMapPresent;
        {{GetDisplacementFunction}}
        half4 main(float2 coord) {
            if (uMapPresent == 0) return src.eval(coord);
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
        MapChild? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.FullFrame,
            u => mapChild.BindPresence(
                u.DensityScaledFloat2("uTranslation", r.X, r.Y)
                 .Int("uChannel", (int)channel)
                 .Int("uSigned", signed ? 1 : 0)),
            children: [mapChild.Binding],
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
        uniform int uMapPresent;
        {{GetDisplacementFunction}}
        half4 main(float2 coord) {
            if (uMapPresent == 0) return src.eval(coord);
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
        MapChild? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.FullFrame,
            u => mapChild.BindPresence(
                u.Float2("uScale", r.Scale * r.ScaleX / 10000, r.Scale * r.ScaleY / 10000)
                 .Deferred("uPivot", (shaderBuilder, name, context) =>
                 {
                     shaderBuilder.Uniforms[name] = new SKPoint(
                         (context.TargetBounds.Width / 2 + r.CenterX) * context.WorkingScale,
                         (context.TargetBounds.Height / 2 + r.CenterY) * context.WorkingScale);
                 })
                 .Int("uChannel", (int)channel)
                 .Int("uSigned", signed ? 1 : 0)),
            children: [mapChild.Binding],
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
        uniform int uMapPresent;
        {{GetDisplacementFunction}}
        half4 main(float2 coord) {
            if (uMapPresent == 0) return src.eval(coord);
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
        MapChild? mapChild = BuildMapChild(builder, displacementMap);
        if (mapChild is null)
            return;

        builder.Shader(ShaderNodeDescriptor.WholeSource(
            s_describeSource,
            BoundsContract.FullFrame,
            u => mapChild.BindPresence(
                u.Float("uAngle", MathUtilities.Deg2Rad(r.Rotation))
                 .Deferred("uPivot", (shaderBuilder, name, context) =>
                 {
                     shaderBuilder.Uniforms[name] = new SKPoint(
                         (context.TargetBounds.Width / 2 + r.CenterX) * context.WorkingScale,
                         (context.TargetBounds.Height / 2 + r.CenterY) * context.WorkingScale);
                 })
                 .Int("uChannel", (int)channel)
                 .Int("uSigned", signed ? 1 : 0)),
            children: [mapChild.Binding],
            srcTileMode: spreadMethod.ToSKShaderTileMode()));
    }
}
