namespace Beutl.NodeTree;

public interface INodeMonitor : INodeItem
{
    NodeMonitorContentKind ContentKind { get; }

    bool IsEnabled { get; set; }

    event EventHandler? ContentChanged;
}
