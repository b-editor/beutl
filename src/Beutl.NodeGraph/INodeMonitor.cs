namespace Beutl.NodeGraph;

public interface INodeMonitor : INodeMember
{
    NodeMonitorContentKind ContentKind { get; }

    bool IsEnabled { get; set; }

    bool IsBusy => false;

    string? BusyText => null;

    event EventHandler? ContentChanged;
}
