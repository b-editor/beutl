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
    /// (<c>src.eval(fragCoord)</c> with no coordinate offset). When set, the node is described as
    /// coordinate-invariant: it gets identity bounds by construction and may participate in fusion. Setting this on
    /// a script that samples neighbours produces wrong output by contract — the single-pixel rule is the author's
    /// responsibility. Left off, the script runs as a non-invariant whole-source pass.
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
        float w = builder.WorkingScale;
        (int devW, int devH) = RenderNodeContext.DeviceBufferSize(builder.Bounds, w);

        void BindUniforms(UniformBindingBuilder u)
        {
            if (effect.Uniforms.Contains("progress")) u.Float("progress", progress);
            if (effect.Uniforms.Contains("duration")) u.Float("duration", duration);
            if (effect.Uniforms.Contains("time")) u.Float("time", time);
            if (effect.Uniforms.Contains("width")) u.Float("width", devW);
            if (effect.Uniforms.Contains("height")) u.Float("height", devH);
            if (effect.Uniforms.Contains("iResolution")) u.Float2("iResolution", devW, devH);
            if (effect.Uniforms.Contains("iScale")) u.Float("iScale", w);
            if (effect.Uniforms.Contains("iTime")) u.Float("iTime", time);
        }

        builder.Shader(r.CoordinateInvariant
            ? ShaderNodeDescriptor.WholeSourceInvariant(source, BindUniforms)
            : ShaderNodeDescriptor.WholeSource(source, BoundsContract.Identity, BindUniforms));
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
            BoundsContract.Identity,
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
