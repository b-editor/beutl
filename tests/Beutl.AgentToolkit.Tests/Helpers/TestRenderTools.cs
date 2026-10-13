using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class TestRenderTools
{
    public static RenderTools Create(
        AgentSessionManager manager,
        EncoderRegistration? encoders = null,
        RenderJobManager? renderJobs = null,
        IOutputOperationLeaseProvider? outputOperations = null)
    {
        var stillRenderer = new StillRenderer();
        return new RenderTools(
            manager,
            new DestructiveGuard(),
            stillRenderer,
            new StoryboardRenderer(),
            new FrameDifferenceAnalyzer(stillRenderer),
            new AudioRhythmAnalyzer(),
            new VideoExporter(encoders ?? new EncoderRegistration()),
            renderJobs ?? new RenderJobManager(),
            outputOperations ?? StandaloneOutputOperationLeaseProvider.Instance);
    }
}
