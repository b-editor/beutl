using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;

using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class NodeViewModel : IDisposable
{
    public NodeViewModel(INode node)
    {
        Node = node;
    }

    public INode Node { get; }

    public ReactivePropertySlim<Point> Position { get; } = new();

    public void Dispose()
    {

    }

    public void NotifyPositionChange(Point point)
    {
        Position.Value = point;
    }
}
