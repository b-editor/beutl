using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Shaders;
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
    public IExpressionProperty<string> FragmentShader { get; } = Property.CreateExpression(GetDefaultShader());

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

    // Checks the script exactly as the effect prepares it, so a script reported as compiled also runs.
    public ScriptCompilationResult ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return ScriptCompilationResult.Compiled;

        VulkanShaderCompiler compiler;
        try
        {
            compiler = new VulkanShaderCompiler();
        }
        catch (Exception)
        {
            // Without shaderc the script cannot be checked at all, which is not the script's failure.
            return ScriptCompilationResult.Unavailable;
        }

        try
        {
            using (compiler)
            {
                PrepareProgram(script, compiler);
            }

            return ScriptCompilationResult.Compiled;
        }
        catch (Exception ex)
        {
            return ScriptCompilationResult.Fail(ex.Message);
        }
    }

    // Compiles every stage of the fullscreen program and checks it against the pass that binds it.
    private static ShaderProgram PrepareProgram(string script, VulkanShaderCompiler compiler)
    {
        var program = ShaderProgram.Fullscreen(new ShaderSource(script));
        var interfaces = program.Stages.ToDictionary(pair => pair.Key,
            pair => new ShaderInterface(compiler.CompileToSpirv(pair.Value.Text, pair.Key), pair.Key));
        ShaderInterface.ValidateProgram(interfaces, CreateGraph(program, default).Passes[0]);
        return program;
    }

    private static ShaderGraphDescription CreateGraph(ShaderProgram program, PushConstants constants)
        => ShaderGraphBuilder.Build((Program: program, Constants: constants), static (graph, state) =>
        {
            ShaderImage output = graph.Image2D("script", graph.Source);
            output.RasterSource = graph.Source;
            graph.Fullscreen("script", state.Program, pass =>
            {
                pass.SampledImage(0, graph.Source, SamplerState.LinearClamp);
                pass.ColorAttachment(0, output);
                pass.PushConstants(state.Constants);
                pass.ImageSizeConstants(output, 12);
            });
            return graph.Output(output);
        });

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
        internal ShaderProgram? _program;
        private string? _recordedShader;

        private ScriptTimeline _timeline;

        public float Progress => _timeline.Progress;

        public float Duration => _timeline.Duration;

        public float Time => _timeline.Time;

        partial void PostReconcile(GLSLScriptEffect obj, CompositionContext context)
        {
            if (_timeline.Update(obj, context))
            {
                Version++;
            }

            if (_recordedShader != FragmentShader)
            {
                _recordedShader = null;
                _program = null;
                if (string.IsNullOrWhiteSpace(FragmentShader))
                {
                    _recordedShader = FragmentShader;
                    return;
                }
                try
                {
                    using var compiler = new VulkanShaderCompiler();
                    _recordedShader = FragmentShader;
                    _program = PrepareProgram(FragmentShader, compiler);
                    Version++; // A successful retry must invalidate an already recorded pass-through.
                }
                catch (Exception ex)
                {
                    // Keep user-script preparation failures local, including unsupported reflection expressions.
                    // Cache only after compiler initialization succeeds; renderer pipeline/execution errors remain visible.
                    s_logger.LogError(ex, "Failed to prepare the GLSL script; the effect is inactive.");
                }
            }
        }

        protected override void ApplyTo(FilterEffectContext context)
        {
            if (_program == null)
                return;

            context.ShaderGraph(CreateGraph(_program, new PushConstants
            {
                Progress = Progress,
                Duration = Duration,
                Time = Time
            }));
        }
    }
}
