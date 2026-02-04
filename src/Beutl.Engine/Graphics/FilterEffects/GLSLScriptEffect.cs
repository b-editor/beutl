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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._pipeline == null)
            return;

        context.CustomEffect(
            (Resource: r.Progress, duration: r.Duration, time: r.Time, pipeline: r._pipeline,
                compileError: r._compileError),
            OnApplyTo,
            static (_, r) => r);
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, GLSLFilterPipeline pipeline, string? compileError) data,
        CustomFilterEffectContext c)
    {
        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
            return;

        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            RenderTarget? renderTarget = effectTarget.RenderTarget;

            if (renderTarget == null)
                continue;

            ITexture2D? sourceTexture = renderTarget.Texture;
            if (sourceTexture == null)
                continue;

            // Prepare source for sampling
            renderTarget.PrepareForSampling();

            // Create new render target for output
            EffectTarget newTarget = c.CreateTarget(effectTarget.Bounds);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            ITexture2D destinationTexture = newRenderTarget.Texture;

            // Create depth texture (required by CreateFramebuffer3D)
            using ITexture2D depthTexture = context.CreateTexture2D(
                destinationTexture.Width,
                destinationTexture.Height,
                TextureFormat.Depth32Float);

            // Prepare push constants
            var pushConstants = new PushConstants
            {
                Progress = data.progress,
                Duration = data.duration,
                Time = data.time,
                Width = effectTarget.Bounds.Width,
                Height = effectTarget.Bounds.Height
            };

            // Execute filter
            data.pipeline.Execute(sourceTexture, destinationTexture, depthTexture, pushConstants);

            // Replace target
            effectTarget.Dispose();
            c.Targets[i] = newTarget;
        }
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
        internal GLSLFilterPipeline? _pipeline;
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

            _pipeline?.Dispose();
            _pipeline = null;
            var prevError = _compileError;
            _compileError = null;
            _compiledShader = shader;

            if (string.IsNullOrWhiteSpace(shader))
                return;

            IGraphicsContext? context = GraphicsContextFactory.SharedContext;
            if (context == null || !context.Supports3DRendering)
            {
                _compileError = "Vulkan 3D rendering is not supported on this platform.";
                if (prevError != _compileError)
                {
                    s_logger.LogWarning(_compileError);
                }
                return;
            }

            _pipeline = GLSLFilterPipeline.Create(context, shader);
            if (_pipeline == null)
            {
                _compileError = "Failed to compile GLSL shader.";
                if (prevError != _compileError)
                {
                    s_logger.LogError("Failed to compile GLSL shader");
                }
            }
        }

        partial void PostDispose(bool disposing)
        {
            _pipeline?.Dispose();
            _pipeline = null;
            _compileError = null;
        }
    }
}
