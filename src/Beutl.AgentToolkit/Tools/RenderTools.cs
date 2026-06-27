using System.ComponentModel;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

[McpServerToolType]
public sealed class RenderTools(
    AgentSessionManager sessions,
    IWorkspaceGuard workspace,
    DestructiveGuard destructiveGuard,
    StillRenderer stillRenderer,
    VideoExporter videoExporter) : ToolBase
{
    [McpServerTool(Name = "render_still")]
    [Description("Renders a still PNG from the current scene to a workspace-relative output path.")]
    public ValueTask<ToolResult<RenderStillResponse>> RenderStill(
        [Description("Workspace-relative or in-workspace absolute output path. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Scene time in seconds.")]
        double timeSeconds = 0,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            Scene scene = RequireScene();
            string resolvedPath = workspace.ResolveForWrite(outputPath);
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            return await stillRenderer.RenderAsync(
                scene,
                TimeSpan.FromSeconds(Math.Max(0, timeSeconds)),
                resolvedPath,
                renderScale,
                cancellationToken).ConfigureAwait(false);
        });
    }

    [McpServerTool(Name = "export_video")]
    [Description("Exports the current scene through a registered headless encoder to a workspace-relative output path.")]
    public ValueTask<ToolResult<ExportVideoResponse>> ExportVideo(
        [Description("Workspace-relative or in-workspace absolute output path. Existing files require confirmOverwrite.")]
        string outputPath,
        [Description("Frame-rate numerator.")]
        int frameRateNumerator = 30,
        [Description("Frame-rate denominator.")]
        int frameRateDenominator = 1,
        [Description("Audio sample rate.")]
        int sampleRate = 44100,
        [Description("Supersampling render scale. Values <= 0 use 1.")]
        float renderScale = 1,
        [Description("Required when outputPath already exists.")]
        bool confirmOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            if (frameRateNumerator <= 0 || frameRateDenominator <= 0)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "Frame-rate numerator and denominator must be positive."));
            }

            Scene scene = RequireScene();
            string resolvedPath = workspace.ResolveForWrite(outputPath);
            destructiveGuard.EnsureOverwriteAllowed(resolvedPath, confirmOverwrite);
            return await videoExporter.ExportAsync(
                scene,
                resolvedPath,
                new Rational(frameRateNumerator, frameRateDenominator),
                sampleRate,
                renderScale,
                cancellationToken).ConfigureAwait(false);
        });
    }

    private Scene RequireScene()
    {
        IEditingSession session = sessions.RequireSession();
        if (session.Root is Scene scene)
        {
            return scene;
        }

        throw new ReconcileException(new ToolError(
            ErrorCode.ValidationRejected,
            "The current editing session is not attached to a scene."));
    }
}
