namespace Beutl.NodeGraph;

public interface INodeMonitor : INodeMember
{
    NodeMonitorContentKind ContentKind { get; }

    bool IsEnabled { get; set; }

    event EventHandler? ContentChanged;
}
