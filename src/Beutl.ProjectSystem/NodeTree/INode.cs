using Beutl.Media;

namespace Beutl.NodeTree;

public interface INode : ILogicalElement, IAffectsRender
{
    IReadOnlyList<INodeItem> Items { get; }

    event EventHandler? NodeTreeInvalidated;

    // 1. ItemsのIInputSocket.Connection.Nodeを評価する。
    // 2. IOutputSocket.ConnectionsからIInputSocketにデータを送る (Receive)
    void Evaluate(EvaluationContext context);
}
