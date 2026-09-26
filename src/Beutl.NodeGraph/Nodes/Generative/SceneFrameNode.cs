using System.Globalization;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Generative;

namespace Beutl.NodeGraph.Nodes.Generative;

/// <summary>
/// The scene as it looks at a moment, captured when the AI nodes run: the node-graph form of
/// the AI tab's "use current frame". Capturing is free and redone on every run; an unchanged
/// frame keeps its result, so the generations fed by it are not bought again.
/// </summary>
/// <remarks>
/// The scene is rendered only while running, when this node outputs its previous capture, so
/// a scene that contains this graph never renders into itself.
/// </remarks>
public sealed partial class SceneFrameNode : GenerativeNode
{
    public SceneFrameNode()
    {
        Output = AddOutput<ImageSourceRenderNode?>("Image", NodePortDisplays.Image);
        UseCurrentTime = AddInput<bool>("UseCurrentTime", NodePortDisplays.UseCurrentTime);
        Time = AddInput<TimeSpan>("Time", NodePortDisplays.Time);
        AddGenerativeMonitors(NodePortDisplays.Preview, NodePortDisplays.Status);
        UseCurrentTime.Property?.SetValue(true);
    }

    public override GenerativeOperation Operation => GenerativeOperation.SceneFrame;

    public override string CatalogOperationId => string.Empty;

    public override bool AlwaysRun => true;

    public OutputPort<ImageSourceRenderNode?> Output { get; }

    /// <summary>Capture at the playhead, as the AI tab's "use current frame" does.</summary>
    public InputPort<bool> UseCurrentTime { get; }

    public InputPort<TimeSpan> Time { get; }

    protected internal override GenerativeRequest BuildRequest(GraphNode.Resource resource, GraphCompositionContext context)
    {
        var r = (Resource)resource;
        TimeSpan time = r.UseCurrentTime ? context.Time : r.Time;
        return new SceneFrameNodeRequest(this)
        {
            Time = time,
            RequestKeySeed = RequestKeySeed,
            ParameterFingerprint = GenerativeFingerprint.Combine([time.Ticks.ToString(CultureInfo.InvariantCulture)]),
        };
    }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = UpdateActiveImageOutput(context);
            // Never stale by its inputs: whether the scene changed is only known by capturing it.
            RequireOriginal().ReportParameterFingerprint(RequireOriginal().ActiveGeneration?.ParameterFingerprint ?? string.Empty);
        }
    }
}
