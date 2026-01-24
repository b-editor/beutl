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

    private SKRuntimeEffect? _runtimeEffect;
    private string? _compiledScript;
    private string? _compileError;

    public SKSLScriptEffect()
    {
        ScanProperties<SKSLScriptEffect>();
    }

    [Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
    public IProperty<string> Script { get; } = Property.Create(GetDefaultScript());

    private static string GetDefaultScript()
    {
        return """
            uniform shader src;
            uniform float progress;  // 0.0 - 1.0
            uniform float duration;  // seconds
            uniform float time;      // seconds

            half4 main(float2 fragCoord) {
                half4 c = src.eval(fragCoord);
                return c;
            }
            """;
    }

    private void CompileScript(string script)
    {
        if (_compiledScript == script)
            return;

        _runtimeEffect?.Dispose();
        _runtimeEffect = null;
        _compileError = null;
        _compiledScript = script;

        if (string.IsNullOrWhiteSpace(script))
            return;

        _runtimeEffect = SKRuntimeEffect.CreateShader(script, out string? errorText);
        if (errorText is not null)
        {
            s_logger.LogError("Failed to compile SKSL: {ErrorText}", errorText);
            _compileError = errorText;
        }
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        CompileScript(r.Script);

        if (_runtimeEffect == null)
            return;

        context.CustomEffect(
            (progress: r.Progress, duration: r.Duration, time: r.Time, effect: _runtimeEffect),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, SKRuntimeEffect effect) data,
        CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            var renderTarget = effectTarget.RenderTarget!;

            using var image = renderTarget.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            var builder = new SKRuntimeShaderBuilder(data.effect);

            builder.Children["src"] = baseShader;
            builder.Uniforms["progress"] = data.progress;
            builder.Uniforms["duration"] = data.duration;
            builder.Uniforms["time"] = data.time;

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
        private float _progress;
        private float _duration;
        private float _time;

        public float Progress => _progress;

        public float Duration => _duration;

        public float Time => _time;

        partial void PostUpdate(SKSLScriptEffect obj, RenderContext context)
        {
            float duration = (float)obj.TimeRange.Duration.TotalSeconds;
            float time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            float progress = duration > 0 ? time / duration : 0;

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (_duration != duration || _time != time || _progress != progress)
            {
                Version++;
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator

            _duration = duration;
            _time = time;
            _progress = progress;
        }
    }
}
