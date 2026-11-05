using System.ComponentModel.DataAnnotations;
using Beutl.Language;

namespace Beutl.NodeGraph.Nodes.Shaders;

internal static class ShaderPortDisplays
{
    internal static DisplayAttribute Get(string name) => new()
    {
        Name = "ShaderPort_" + name,
        ResourceType = typeof(NodeGraphStrings)
    };
}
