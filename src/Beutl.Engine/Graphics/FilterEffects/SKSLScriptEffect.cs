using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.SKSLScriptEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SKSLScriptEffect : FilterEffect, IScriptCompilableEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<SKSLScriptEffect>();

    public SKSLScriptEffect()
    {
        ScanProperties<SKSLScriptEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Script), ResourceType = typeof(GraphicsStrings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string> Script { get; } = Property.Create(GetDefaultScript());

    /// <summary>
    /// Opt-in assertion (contract A6/A3) that the script samples only the current pixel
    /// (<c>src.eval(fragCoord)</c> with no coordinate offset). When set, the node is described as a
    /// coordinate-invariant whole-source shader: it gets identity bounds and identity ROI by construction, so it
    /// never inflates buffers or blocks ROI propagation. It stays its own pass either way — fusion requires a
    /// snippet, and a script is always whole-source. Setting this on a script that samples neighbours produces
    /// wrong output by contract — the single-pixel rule is the author's responsibility.
    /// </summary>
    [Display(Name = "Coordinate invariant")]
    public IProperty<bool> CoordinateInvariant { get; } = Property.Create(false);

    private static string GetDefaultScript()
    {
        return """
               uniform shader src;
               uniform float progress;  // 0.0 - 1.0
               uniform float duration;  // seconds
               uniform float time;      // seconds
               uniform float width;     // render target width (device px)
               uniform float height;    // render target height (device px)
               // Also available:
               // uniform float2 iResolution;  // (width, height) in device px
               // uniform float  iScale;       // working density (device px per logical px)
               // uniform float  iTime;

               half4 main(float2 fragCoord) {
                   half4 c = src.eval(fragCoord);
                   return c;
               }
               """;
    }

    public ScriptCompilationResult ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return ScriptCompilationResult.Compiled;

        try
        {
            using var effect = SKRuntimeEffect.CreateShader(script, out string? errorText);
            return string.IsNullOrEmpty(errorText)
                ? ScriptCompilationResult.Compiled
                : ScriptCompilationResult.Fail(errorText);
        }
        catch (Exception ex)
        {
            return ScriptCompilationResult.Fail(ex.Message);
        }
    }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r._shader == null)
            return;

        SKRuntimeEffect effect = r._shader.Effect;
        string source = r._compiledScript!;

        if (!effect.Children.Contains("src"))
        {
            // A script with no `src` child never samples the source — it is a pure generator. The whole-source path
            // requires the implicit `src` binding, so it runs as a geometry pass drawing the built shader over the
            // input rect (the legacy behavior for such scripts).
            DescribeGenerator(builder, r);
            return;
        }

        float progress = r.Progress;
        float duration = r.Duration;
        float time = r.Time;

        // The resolution/density uniforms (width/height/iResolution/iScale) are late-bound to the pass's
        // execution-time buffer and working scale (execution-plan §C3.2): the budget re-clamp — and, for a
        // non-invariant script, the monotonic carry from an upstream forward-inflating pass — can execute the
        // script below the describe-time working scale, and RenderTime alone does not protect against that. The
        // time/progress uniforms are density-independent and stay describe-time.
        void BindUniforms(UniformBindingBuilder u)
        {
            if (effect.Uniforms.Contains("progress")) u.Float("progress", progress);
            if (effect.Uniforms.Contains("duration")) u.Float("duration", duration);
            if (effect.Uniforms.Contains("time")) u.Float("time", time);
            if (effect.Uniforms.Contains("width")) u.Deferred("width", static (b, name, ctx) => b.Uniforms[name] = (float)ctx.TargetWidth);
            if (effect.Uniforms.Contains("height")) u.Deferred("height", static (b, name, ctx) => b.Uniforms[name] = (float)ctx.TargetHeight);
            if (effect.Uniforms.Contains("iResolution")) u.Deferred("iResolution", static (b, name, ctx) => b.Uniforms[name] = new[] { (float)ctx.TargetWidth, (float)ctx.TargetHeight });
            if (effect.Uniforms.Contains("iScale")) u.Deferred("iScale", static (b, name, ctx) => b.Uniforms[name] = ctx.WorkingScale);
            if (effect.Uniforms.Contains("iTime")) u.Float("iTime", time);
        }

        // A non-invariant script may sample non-locally (src.eval(fragCoord + offset)); a downstream deflating pass
        // (a fixed Clipping) would ROI-crop an Identity-bounds bake to a sub-rect and shift/clip those samples
        // (contract A3). RenderTime keeps it baking full-frame. The CoordinateInvariant opt-in asserts single-pixel
        // sampling, so it keeps identity bounds and participates in ROI propagation by construction.
        builder.Shader(r.CoordinateInvariant
            ? ShaderNodeDescriptor.WholeSourceInvariant(source, BindUniforms)
            : ShaderNodeDescriptor.WholeSource(source, BoundsContract.RenderTime, BindUniforms));
    }

    private static void DescribeGenerator(EffectGraphBuilder builder, Resource r)
    {
        SKSLShader shader = r._shader!;
        float progress = r.Progress;
        float duration = r.Duration;
        float time = r.Time;

        builder.Geometry(GeometryNodeDescriptor.Create(
            session =>
            {
                ImmediateCanvas canvas = session.OpenCanvas();
                SKRuntimeShaderBuilder shaderBuilder = shader.CreateBuilder();
                SKRuntimeEffect effect = shader.Effect;

                // Resolution uniforms report device px at the output buffer's real (possibly clamped) density.
                float w = canvas.Density;
                (int devW, int devH) = RenderNodeContext.DeviceBufferSize(session.Bounds, w);
                if (effect.Uniforms.Contains("progress")) shaderBuilder.Uniforms["progress"] = progress;
                if (effect.Uniforms.Contains("duration")) shaderBuilder.Uniforms["duration"] = duration;
                if (effect.Uniforms.Contains("time")) shaderBuilder.Uniforms["time"] = time;
                if (effect.Uniforms.Contains("width")) shaderBuilder.Uniforms["width"] = (float)devW;
                if (effect.Uniforms.Contains("height")) shaderBuilder.Uniforms["height"] = (float)devH;
                if (effect.Uniforms.Contains("iResolution")) shaderBuilder.Uniforms["iResolution"] = new SKPoint(devW, devH);
                if (effect.Uniforms.Contains("iScale")) shaderBuilder.Uniforms["iScale"] = w;
                if (effect.Uniforms.Contains("iTime")) shaderBuilder.Uniforms["iTime"] = time;

                using SKShader built = shaderBuilder.Build();
                using var paint = new SKPaint { Shader = built };
                using (canvas.PushDeviceSpace())
                {
                    canvas.Canvas.DrawRect(new SKRect(0, 0, devW, devH), paint);
                }
            },
            // The generated pattern is anchored to the FULL output rect: fragCoord/width/height are the pass buffer's
            // device coordinates (A3). Identity would let a downstream deflating pass ROI-crop this to an OFFSET
            // sub-rect with a LOCAL fragCoord origin and a sub-rect width/height, rescaling and shifting the pattern.
            // RenderTime keeps it baking full-frame — the BlendEffect / DisplacementMap show-map precedent.
            BoundsContract.RenderTime,
            structuralToken: nameof(SKSLScriptEffect) + ".Generator"));
    }

    public new partial class Resource
    {
        internal SKSLShader? _shader;
        internal string? _compiledScript;
        internal string? _compileError;

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(SKSLScriptEffect obj, CompositionContext context)
        {
            float duration = (float)obj.TimeRange.Duration.TotalSeconds;
            float time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            float progress = duration > 0 ? time / duration : 0;

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (Duration != duration || Time != time || Progress != progress)
            {
                Version++;
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator

            Duration = duration;
            Time = time;
            Progress = progress;
            CompileScript(Script);
        }

        private void CompileScript(string script)
        {
            if (_compiledScript == script)
                return;

            _shader?.Dispose();
            _shader = null;
            var prevError = _compileError;
            _compileError = null;
            _compiledScript = script;

            if (string.IsNullOrWhiteSpace(script))
                return;

            if (!SKSLShader.TryCreate(script, out _shader, out string? errorText))
            {
                _compileError = errorText;
                if (prevError != _compileError)
                {
                    s_logger.LogError("Failed to compile SKSL script: {Error}", errorText);
                }
            }
        }

        partial void PostDispose(bool disposing)
        {
            _shader?.Dispose();
            _shader = null;
            _compileError = null;
        }
    }
}
