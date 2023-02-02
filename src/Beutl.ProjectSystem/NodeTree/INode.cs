using Beutl.Collections;
using Beutl.Media;

namespace Beutl.NodeTree;

public interface INode : ILogicalElement, IAffectsRender
{
    ICoreList<INodeItem> Items { get; }

    (double X, double Y) Position { get; }

    event EventHandler? NodeTreeInvalidated;

    // 1. ItemsのIInputSocket.Connection.Nodeを評価する。
    // 2. IOutputSocket.ConnectionsからIInputSocketにデータを送る (Receive)
    void Evaluate(EvaluationContext context);
}
