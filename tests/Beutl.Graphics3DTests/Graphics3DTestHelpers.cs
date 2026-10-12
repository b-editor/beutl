using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

/// <summary>Shaders and request plumbing shared by the GPU tests in this project.</summary>
internal static class Graphics3DTestHelpers
{
    /// <summary>Builds its own fullscreen triangle, so a pipeline using it declares no vertex input.</summary>
    public const string FullscreenVertexShader = """
        #version 450

        layout(location = 0) out vec2 fragCoord;

        void main() {
            vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            vec2 uvs[3] = vec2[](vec2(0.0, 0.0), vec2(2.0, 0.0), vec2(0.0, 2.0));
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = uvs[gl_VertexIndex];
        }
        """;

    /// <summary>Outputs the texture at binding 0 unchanged.</summary>
    public const string PassthroughFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D sourceTexture;

        void main() {
            outColor = texture(sourceTexture, fragCoord);
        }
        """;

    /// <summary>Records <paramref name="root"/> under a request with <paramref name="options"/> and compiles it.</summary>
    public static CompiledRenderRequest Compile(RenderNode root, RenderRequestOptions options)
    {
        var request = new RenderRequest(options);
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
            return new RenderRequestCompiler().Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Executes <paramref name="compiled"/> onto a target leased from <paramref name="pool"/> that covers
    /// <paramref name="targetBounds"/>, and reads the target back.
    /// </summary>
    /// <param name="translateToTarget">Whether the canvas is translated so the target's origin is its origin.</param>
    /// <param name="createExecutor">Creates the executor, or <see langword="null"/> for a default one.</param>
    /// <param name="observeExecution">Starts an observation that lasts for the execution.</param>
    public static (ushort[] Pixels, RenderExecutionStatistics Statistics) ExecuteIntoPool(
        RenderTargetPool pool,
        CompiledRenderRequest compiled,
        Rect targetBounds,
        Rect replayBounds,
        float density,
        float maxWorkingScale,
        bool translateToTarget = false,
        Func<RenderTargetLeaseSession, RenderRequestExecutor>? createExecutor = null,
        Func<IDisposable>? observeExecution = null)
    {
        using RenderTargetLeaseSession targets = pool.BeginSession(RenderIntent.Preview);
        using RenderTargetLease output = targets.Acquire(PixelRect.FromRect(targetBounds, density).Size);
        using var canvas = new ImmediateCanvas(
            output.Target,
            RenderIntent.Preview,
            density,
            maxWorkingScale,
            targetBounds.Size);
        using (translateToTarget
                   ? canvas.PushTransform(Matrix.CreateTranslation(-targetBounds.X, -targetBounds.Y))
                   : default)
        {
            canvas.Clear();
            RenderRequestExecutor executor = createExecutor?.Invoke(targets) ?? new RenderRequestExecutor(targets);
            using (observeExecution?.Invoke())
                executor.Execute(compiled, canvas, replayBounds: replayBounds);
            using Bitmap rendered = output.Target.Snapshot();
            return (rendered.GetPixelSpan<ushort>().ToArray(), executor.Statistics);
        }
    }
}
