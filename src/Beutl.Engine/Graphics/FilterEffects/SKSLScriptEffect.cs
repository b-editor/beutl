using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Validation;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using ValidationContext = Beutl.Validation.ValidationContext;

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._runtimeEffect == null)
            return;

        context.CustomEffect(
            (Resource: r.Progress, duration: r.Duration, time: r.Time, effect: r._runtimeEffect,
                compileError: r._compileError),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, SKRuntimeEffect effect, string? compileError) data,
        CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            var builder = new SKRuntimeShaderBuilder(data.effect);

            if (data.effect.Children.Contains("src"))
                builder.Children["src"] = baseShader;
            if (data.effect.Uniforms.Contains("progress"))
                builder.Uniforms["progress"] = data.progress;
            if (data.effect.Uniforms.Contains("duration"))
                builder.Uniforms["duration"] = data.duration;
            if (data.effect.Uniforms.Contains("time"))
                builder.Uniforms["time"] = data.time;
            if (data.effect.Uniforms.Contains("width"))
                builder.Uniforms["width"] = effectTarget.Bounds.Width;
            if (data.effect.Uniforms.Contains("height"))
                builder.Uniforms["height"] = effectTarget.Bounds.Height;
            if (data.effect.Uniforms.Contains("iResolution"))
                builder.Uniforms["iResolution"] = new SKPoint(effectTarget.Bounds.Width, effectTarget.Bounds.Height);
            if (data.effect.Uniforms.Contains("iTime"))
                builder.Uniforms["iTime"] = data.time;

            var newTarget = c.CreateTarget(effectTarget.Bounds);

            using (SKShader finalShader = builder.Build())
            using (var paint = new SKPaint())
            using (var canvas = c.Open(newTarget))
            {
                paint.Shader = finalShader;
                canvas.Clear();
                canvas.Canvas.DrawRect(
                    new SKRect(0, 0, effectTarget.Bounds.Width, effectTarget.Bounds.Height),
                    paint);

                c.Targets[i] = newTarget;
            }

            effectTarget.Dispose();
        }
    }

    public new partial class Resource
    {
        internal SKRuntimeEffect? _runtimeEffect;
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

            _runtimeEffect?.Dispose();
            _runtimeEffect = null;
            var prevError = _compileError;
            _compileError = null;
            _compiledScript = script;

            if (string.IsNullOrWhiteSpace(script))
                return;

            _runtimeEffect = SKRuntimeEffect.CreateShader(script, out string? errorText);
            if (errorText is not null)
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
            _runtimeEffect?.Dispose();
            _runtimeEffect = null;
            _compileError = null;
        }
    }
}
