using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed partial class SKSLScriptEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<SKSLScriptEffect>();

    public SKSLScriptEffect()
    {
        ScanProperties<SKSLScriptEffect>();
    }

    [Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string> Script { get; } = Property.Create(GetDefaultScript());

    private static string GetDefaultScript()
    {
        return """
               uniform shader src;
               uniform float progress;  // 0.0 - 1.0
               uniform float duration;  // seconds
               uniform float time;      // seconds
               uniform float width;     // render target width
               uniform float height;    // render target height
               // Also available:
               // uniform float2 iResolution;
               // uniform float iTime;

               half4 main(float2 fragCoord) {
                   half4 c = src.eval(fragCoord);
                   return c;
               }
               """;
    }

    internal static string? ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        try
        {
            using var effect = SKRuntimeEffect.CreateShader(script, out string? errorText);
            return errorText;
        }
        catch (Exception ex)
        {
            return ex.Message;
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
            using var effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = image.ToShader();

            var builder = data.shader.CreateBuilder();
            var effect = data.shader.Effect;

            if (effect.Children.Contains("src"))
                builder.Children["src"] = baseShader;
            if (effect.Uniforms.Contains("progress"))
                builder.Uniforms["progress"] = data.progress;
            if (effect.Uniforms.Contains("duration"))
                builder.Uniforms["duration"] = data.duration;
            if (effect.Uniforms.Contains("time"))
                builder.Uniforms["time"] = data.time;
            if (effect.Uniforms.Contains("width"))
                builder.Uniforms["width"] = effectTarget.Bounds.Width;
            if (effect.Uniforms.Contains("height"))
                builder.Uniforms["height"] = effectTarget.Bounds.Height;
            if (effect.Uniforms.Contains("iResolution"))
                builder.Uniforms["iResolution"] = new SKPoint(effectTarget.Bounds.Width, effectTarget.Bounds.Height);
            if (effect.Uniforms.Contains("iTime"))
                builder.Uniforms["iTime"] = data.time;

            // 新しいターゲットに適用
            c.Targets[i] = data.shader.ApplyToNewTarget(c, builder, effectTarget.Bounds);
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

        partial void PostUpdate(SKSLScriptEffect obj, RenderContext context)
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
