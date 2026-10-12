using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Common;

internal static class ToolResultExtensions
{
    // The ToolResult<T> JSON every tool returns as its text content. Extra blocks follow a success only.
    public static CallToolResult ToCallToolResult<T>(this ToolResult<T> result, IEnumerable<ContentBlock>? extra = null)
    {
        var content = new List<ContentBlock>
        {
            new TextContentBlock { Text = JsonSerializer.Serialize(result, JsonSerializerOptions.Web) }
        };
        if (result.IsSuccess && extra is not null)
        {
            content.AddRange(extra);
        }

        return new CallToolResult
        {
            Content = content,
            IsError = false
        };
    }
}
