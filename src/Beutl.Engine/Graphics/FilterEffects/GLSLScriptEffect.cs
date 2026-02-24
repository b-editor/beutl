using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.GLSLScriptEffect), ResourceType = typeof(Strings))]
public sealed partial class GLSLScriptEffect : FilterEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<GLSLScriptEffect>();

    public GLSLScriptEffect()
    {
        ScanProperties<GLSLScriptEffect>();
    }

    [Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string> FragmentShader { get; } = Property.Create(GetDefaultShader());

    private static string GetDefaultShader()
    {
        return """
               #version 450

               layout(location = 0) in vec2 fragCoord; // 0.0 - 1.0
               layout(location = 0) out vec4 outColor;

               layout(set = 0, binding = 0) uniform sampler2D srcTexture;

               layout(push_constant) uniform PushConstants {
                   float progress;   // 0.0 - 1.0
                   float duration;   // seconds
                   float time;       // seconds
                   float width;      // render target width
                   float height;     // render target height
               } pc;

               void main() {
                   vec4 c = texture(srcTexture, fragCoord);
                   outColor = c;
               }
               """;
    }

    internal static string? ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        try
        {
            IGraphicsContext? context = GraphicsContextFactory.SharedContext;
            if (context == null)
                return null;

            IShaderCompiler compiler = context.CreateShaderCompiler();
            compiler.CompileToSpirv(script, ShaderStage.Fragment);
            return null;
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
            (progress: r.Progress, duration: r.Duration, time: r.Time, shader: r._shader,
                compileError: r._compileError),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, GLSLShader shader, string? compileError) data,
        CustomFilterEffectContext c)
    {
        data.shader.Apply(c, target => new PushConstants
        {
            Progress = data.progress,
            Duration = data.duration,
            Time = data.time,
            Width = target.Bounds.Width,
            Height = target.Bounds.Height
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Progress;
        public float Duration;
        public float Time;
        public float Width;
        public float Height;
    }

    public new partial class Resource
    {
        internal GLSLShader? _shader;
        internal string? _compiledShader;
        internal string? _compileError;

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(GLSLScriptEffect obj, RenderContext context)
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
            CompileShader(FragmentShader);
        }

        private void CompileShader(string shader)
        {
            if (_compiledShader == shader)
                return;

            _shader?.Dispose();
            _shader = null;
            var prevError = _compileError;
            _compileError = null;
            _compiledShader = shader;

            if (string.IsNullOrWhiteSpace(shader))
                return;

            if (!GLSLShader.TryCreate(shader, out _shader, out string? errorText))
            {
                _compileError = errorText;
                if (prevError != _compileError)
                {
                    s_logger.LogError("Failed to compile GLSL shader: {Error}", errorText);
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
