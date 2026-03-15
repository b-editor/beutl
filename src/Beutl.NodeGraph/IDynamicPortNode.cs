using System.Diagnostics.CodeAnalysis;

namespace Beutl.NodeGraph;

//IGroupInputOutputNode
public interface IDynamicPortNode
{
    NodePortLocation PossibleLocation { get; }

    bool AddNodePort(INodePort port, [NotNullWhen(true)] out Connection? connection);
}
