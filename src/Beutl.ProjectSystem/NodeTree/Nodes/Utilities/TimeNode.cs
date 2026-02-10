using Beutl.ProjectSystem;

namespace Beutl.NodeTree.Nodes.Utilities;

public class TimeNode : Node
{
    private readonly OutputSocket<float> _timeSocket;
    private readonly OutputSocket<float> _startSocket;
    private readonly OutputSocket<float> _durationSocket;
    private readonly OutputSocket<float> _progressSocket;
    private Element? _attachedElement;

    public TimeNode()
    {
        _timeSocket = AddOutput<float>("Time");
        _startSocket = AddOutput<float>("Start");
        _durationSocket = AddOutput<float>("Duration");
        _progressSocket = AddOutput<float>("Progress");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        if (_attachedElement != null)
        {
            float duration = (float)_attachedElement.Length.TotalSeconds;
            float start = (float)_attachedElement.Start.TotalSeconds;
            float time = (float)context.Renderer.Time.TotalSeconds - start;
            float progress = duration > 0 ? Math.Clamp(time / duration, 0, 1) : 0;
            _durationSocket.Value = duration;
            _startSocket.Value = start;
            _timeSocket.Value = time;
            _progressSocket.Value = progress;
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _attachedElement = this.FindHierarchicalParent<Element>();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        _attachedElement = null;
    }
}
