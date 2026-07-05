using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Language;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.GLSLScriptEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class GLSLScriptEffect : FilterEffect, IScriptCompilableEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<GLSLScriptEffect>();

    public GLSLScriptEffect()
    {
        ScanProperties<GLSLScriptEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Script), ResourceType = typeof(GraphicsStrings))]
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
                   float width;      // render target width (device px)
                   float height;     // render target height (device px)
                   float scale;      // working scale w (1.0 = unscaled); multiply absolute-px literals by this
               } pc;

               void main() {
                   vec4 c = texture(srcTexture, fragCoord);
                   outColor = c;
               }
               """;
    }

    public ScriptCompilationResult ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return ScriptCompilationResult.Compiled;

        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null)
            return ScriptCompilationResult.Unavailable;

        try
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            compiler.CompileToSpirv(script, ShaderStage.Fragment);
            return ScriptCompilationResult.Compiled;
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
            (progress: r.Progress, duration: r.Duration, time: r.Time, shader: r._shader,
                compileError: r._compileError),
            OnApplyTo,
            static (_, r) => r);
    }

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r._shader == null)
            return;

        GLSLShader shader = r._shader;
        float progress = r.Progress;
        float duration = r.Duration;
        float time = r.Time;

        // One coordinate-invariant-in-bounds GLSL pass over a pooled destination; without Vulkan the legacy path
        // rendered nothing (a pass-through), so the fallback is identity.
        builder.Compute(ComputeNodeDescriptor.Create(
            ctx =>
            {
                ITexture2D depth = ctx.AcquireDepthScratch();
                ctx.Run(shader, ctx.Source, ctx.Destination, depth, new PushConstants
                {
                    Progress = progress,
                    Duration = duration,
                    Time = time,
                    Width = ctx.Width,
                    Height = ctx.Height,
                    Scale = ctx.WorkingScale,
                });
            },
            passCount: 1,
            ComputeFallback.Identity,
            structuralToken: nameof(GLSLScriptEffect)));
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, GLSLShader shader, string? compileError) data,
        CustomFilterEffectContext c)
    {
        // Push constants report device px at the clamped buffer density.
        data.shader.Apply(c, target =>
        {
            float w = c.ResolveTargetDensity(target.Bounds);
            (int devW, int devH) = CustomFilterEffectContext.DeviceBufferSize(target.Bounds, w);
            return new PushConstants
            {
                Progress = data.progress,
                Duration = data.duration,
                Time = data.time,
                Width = devW,
                Height = devH,
                Scale = w
            };
        });
    }

    // Field order must match the GLSL `layout(push_constant)` block.
    // Total size must stay within VulkanPipeline3D's 128-byte push-constant range.
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Progress;
        public float Duration;
        public float Time;
        public float Width;
        public float Height;
        public float Scale;
    }

    public new partial class Resource
    {
        internal GLSLShader? _shader;
        internal string? _compiledShader;
        internal string? _compileError;

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(GLSLScriptEffect obj, CompositionContext context)
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
