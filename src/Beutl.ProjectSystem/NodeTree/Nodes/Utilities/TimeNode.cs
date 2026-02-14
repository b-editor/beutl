using Beutl.NodeTree.Rendering;
using Beutl.ProjectSystem;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class TimeNode : Node
{
    public TimeNode()
    {
        Time = AddOutput<float>("Time");
        Start = AddOutput<float>("Start");
        Duration = AddOutput<float>("Duration");
        Progress = AddOutput<float>("Progress");
    }

    public OutputSocket<float> Time { get; }

    public new OutputSocket<float> Start { get; }

    public new OutputSocket<float> Duration { get; }

    public OutputSocket<float> Progress { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
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
