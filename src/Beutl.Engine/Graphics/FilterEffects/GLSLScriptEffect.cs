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
            using IDisposable? compilerLifetime = compiler as IDisposable;
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

        if (r._program == null)
            return;

        context.ShaderGraph(CreateGraph(r._program, new PushConstants
        {
            Progress = r.Progress,
            Duration = r.Duration,
            Time = r.Time
        }));
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
                _recordedShader = FragmentShader;
                _program = null;
                if (string.IsNullOrWhiteSpace(FragmentShader))
                    return;
                try
                {
                    var program = ShaderProgram.Fullscreen(new ShaderSource(FragmentShader));
                    using var compiler = new VulkanShaderCompiler();
                    var interfaces = program.Stages.ToDictionary(pair => pair.Key,
                        pair => new ShaderInterface(compiler.CompileToSpirv(pair.Value.Text, pair.Key), pair.Key));
                    ShaderInterface.ValidateProgram(interfaces, CreateGraph(program, default).Passes[0]);
                    _program = program;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    // Invalid edits are inactive until their text changes. Native pipeline allocation and
                    // execution still belong to the renderer and retain the normal graph failure behavior.
                    s_logger.LogError(ex, "Invalid GLSL script; the effect is inactive until the script changes.");
                }
            }
        }
    }
}
