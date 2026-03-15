using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class TimeNode : GraphNode
{
    public TimeNode()
    {
        Time = AddOutput<float>("Time");
        Start = AddOutput<float>("Start");
        Duration = AddOutput<float>("Duration");
        Progress = AddOutput<float>("Progress");
    }

    public OutputPort<float> Time { get; }

    public new OutputPort<float> Start { get; }

    public new OutputPort<float> Duration { get; }

    public OutputPort<float> Progress { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();
            float duration = (float)node.TimeRange.Duration.TotalSeconds;
            float start = (float)node.TimeRange.Start.TotalSeconds;
            float time = (float)context.Time.TotalSeconds - start;
            float progress = duration > 0 ? Math.Clamp(time / duration, 0, 1) : 0;
            Duration = duration;
            Start = start;
            Time = time;
            Progress = progress;
        }
    }
}
