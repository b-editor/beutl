using System.Text.Json;
using Beutl.AgentToolkit.Common;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal static class ToolResultReader
{
    public static ToolResult<T> Read<T>(CallToolResult result)
    {
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();
        return JsonSerializer.Deserialize<ToolResult<T>>(text.Text, JsonSerializerOptions.Web)
               ?? throw new InvalidOperationException("Tool result JSON could not be deserialized.");
    }
}
