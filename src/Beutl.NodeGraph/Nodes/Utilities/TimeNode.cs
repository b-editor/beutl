using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class TimeNode : GraphNode
{
    public TimeNode()
    {
        Time = AddOutput<float>("Time", NodePortDisplays.Time);
        Start = AddOutput<float>("Start", NodePortDisplays.Start);
        Duration = AddOutput<float>("Duration", NodePortDisplays.Duration);
        Progress = AddOutput<float>("Progress", NodePortDisplays.Progress);
    }

    public OutputPort<float> Time { get; }

    public new OutputPort<float> Start { get; }

    public new OutputPort<float> Duration { get; }

    public OutputPort<float> Progress { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            float duration = (float)node.TimeRange.Value.Duration.ToSeconds();
            float start = (float)node.TimeRange.Value.Start.ToSeconds();
            float time = (float)context.Time.ToSeconds() - start;
            float progress = duration > 0 ? Math.Clamp(time / duration, 0, 1) : 0;
            Duration = duration;
            Start = start;
            Time = time;
            Progress = progress;
        }
    }
}
