namespace Beutl.NodeGraph;

public interface INodeMonitor : INodeMember
{
    NodeMonitorContentKind ContentKind { get; }

    bool IsEnabled { get; set; }

    bool IsBusy { get; }

    string? BusyText { get; }

    event EventHandler? ContentChanged;
}
