using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tools;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class McpToolErrorFiltersTests
{
    [Test]
    public void Unknown_tool_argument_returns_validation_error_with_accepted_parameters()
    {
        using JsonDocument schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "outputPath": { "type": "string" },
                "timeSeconds": { "type": "number" },
                "scale": { "type": "number" }
              }
            }
            """);

        CallToolResult? result = McpToolErrorFilters.CreateUnknownArgumentsResultOrNull(
            "render_still",
            ["outputPath", "time"],
            schema.RootElement);
        ToolResult<object?> toolResult = ReadToolResult(result!);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.IsError, Is.Not.True);
            Assert.That(toolResult.IsSuccess, Is.False);
            Assert.That(toolResult.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(toolResult.Error.Target, Is.EqualTo("render_still"));
            Assert.That(toolResult.Error.Message, Does.Contain("Unknown argument"));
            Assert.That(toolResult.Error.Message, Does.Contain("time"));
            Assert.That(toolResult.Error.Message, Does.Contain("outputPath"));
            Assert.That(toolResult.Error.Message, Does.Contain("timeSeconds"));
            Assert.That(toolResult.Error.Message, Does.Contain("scale"));
        });
    }

    [Test]
    public void Accepted_tool_arguments_return_null()
    {
        using JsonDocument schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "outputPath": { "type": "string" },
                "timeSeconds": { "type": "number" }
              }
            }
            """);

        CallToolResult? result = McpToolErrorFilters.CreateUnknownArgumentsResultOrNull(
            "render_still",
            ["outputPath", "timeSeconds"],
            schema.RootElement);

        Assert.That(result, Is.Null);
    }

    private static ToolResult<object?> ReadToolResult(CallToolResult result)
    {
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return JsonSerializer.Deserialize<ToolResult<object?>>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Tool result JSON was empty.");
    }
}
