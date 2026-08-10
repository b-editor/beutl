using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._shader == null)
            return;

        context.CustomEffect(
            (Resource: r.Progress, duration: r.Duration, time: r.Time, shader: r._shader,
                compileError: r._compileError),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, SKSLShader shader, string? compileError) data,
        CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            EffectTarget output = c.CreateTargetLike(effectTarget);
            try
            {
                if (output.RenderTarget is null || output.Scale.IsUnbounded)
                {
                    throw new InvalidOperationException(
                        "The SKSL script effect has no materialized output target: CreateTargetLike could not "
                        + $"replace the {effectTarget.DeviceBounds.Width}x{effectTarget.DeviceBounds.Height} px "
                        + "source because it was not materialized or the replacement allocation failed. "
                        + "The effect fails visibly rather than rendering partially.");
                }

                using SKSLShaderBuilder builder = data.shader.CreateBuilder();

                if (builder.Uniforms.Contains("progress"))
                    builder.Uniforms["progress"] = data.progress;
                if (builder.Uniforms.Contains("duration"))
                    builder.Uniforms["duration"] = data.duration;
                if (builder.Uniforms.Contains("time"))
                    builder.Uniforms["time"] = data.time;

                float w = output.Scale.Value;
                int deviceWidth = output.RenderTarget.Width;
                int deviceHeight = output.RenderTarget.Height;
                if (builder.Uniforms.Contains("width"))
                    builder.Uniforms["width"] = (float)deviceWidth;
                if (builder.Uniforms.Contains("height"))
                    builder.Uniforms["height"] = (float)deviceHeight;
                if (builder.Uniforms.Contains("iResolution"))
                    builder.Uniforms["iResolution"] = new SKPoint(deviceWidth, deviceHeight);
                if (builder.Uniforms.Contains("iScale"))
                    builder.Uniforms["iScale"] = w;
                if (builder.Uniforms.Contains("iTime"))
                    builder.Uniforms["iTime"] = data.time;

                if (builder.Children.Contains("src"))
                {
                    bool rendered = c.UseMappedInputShader(
                        effectTarget,
                        output,
                        (Builder: builder, Shader: data.shader, Context: c, Output: output),
                        static (state, mappedSource) =>
                        {
                            state.Builder.Children["src"] = mappedSource;
                            state.Shader.RenderToTarget(state.Context, state.Builder, state.Output);
                        },
                        SKShaderTileMode.Clamp,
                        SKShaderTileMode.Clamp);
                    if (!rendered)
                    {
                        output.Dispose();
                        continue;
                    }
                }
                else
                {
                    data.shader.RenderToTarget(c, builder, output);
                }

                effectTarget.Dispose();
                c.Targets[i] = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
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
