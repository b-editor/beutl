using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Beutl.Media.Decoding;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class TestRenderTools
{
    public static RenderTools Create(
        AgentSessionManager manager,
        EncoderRegistration? encoders = null,
        RenderJobManager? renderJobs = null,
        IOutputOperationLeaseProvider? outputOperations = null, DecoderRegistry? decoders = null)
    {
        decoders ??= new DecoderRegistry(new());
        var stillRenderer = new StillRenderer(TestFonts.Manager, decoders);
        return new RenderTools(
            manager,
            new DestructiveGuard(),
            stillRenderer,
            new StoryboardRenderer(),
            new FrameDifferenceAnalyzer(stillRenderer),
            new AudioRhythmAnalyzer(decoders),
            new VideoExporter(encoders ?? new EncoderRegistration(), TestFonts.Manager, decoders),
            renderJobs ?? new RenderJobManager(),
            outputOperations ?? StandaloneOutputOperationLeaseProvider.Instance);
    }
}
