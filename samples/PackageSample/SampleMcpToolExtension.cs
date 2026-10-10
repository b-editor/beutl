using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor.Services.Mcp;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace PackageSample;

[Export]
public sealed class SampleMcpToolExtension : McpToolExtension
{
    public override string Name => "SampleMcpToolExtension";

    public override string DisplayName => "SampleMcpToolExtension";

    public override IReadOnlyList<McpToolDefinition> Tools { get; } =
    [
        new McpToolDefinition(
            "sample.count_elements",
            "Counts the elements on one layer of the scene open in the Beutl editor.",
            JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": { "layer": { "type": "integer", "description": "Zero-based layer index." } },
                  "required": ["layer"]
                }
                """).RootElement)
        {
            ReadOnlyHint = true
        }
    ];

    public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
    {
        if (call.EditorContext?.GetService(typeof(Scene)) is not Scene scene)
            return new(McpToolResult.Error("Open a scene in the Beutl editor first."));

        int layer = call.Arguments.GetProperty("layer").GetInt32();
        int count = scene.Children.Count(element => element.ZIndex == layer);
        var value = new JsonObject { ["layer"] = layer, ["count"] = count };
        return new(McpToolResult.Json(JsonSerializer.SerializeToElement(value)));
    }
}
