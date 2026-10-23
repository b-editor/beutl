namespace Beutl.NodeGraph;

public interface INodeMonitor : INodeMember
{
    NodeMonitorContentKind ContentKind { get; }

    IProperty<bool> IsEnabled { get; }

    bool IsBusy { get; }

    string? BusyText { get; }

    event EventHandler? ContentChanged;
}
