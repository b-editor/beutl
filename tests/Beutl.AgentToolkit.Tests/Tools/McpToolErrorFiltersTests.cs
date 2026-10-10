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

    [TestCase("""{ "type": "object", "properties": { "a": {} }, "additionalProperties": true }""")]
    [TestCase("""{ "type": "object", "additionalProperties": { "type": "string" } }""")]
    [TestCase("""{ "type": "object", "properties": { "a": {} }, "patternProperties": { "^x-": {} } }""")]
    [TestCase("""{ "type": "object", "properties": { "a": {} }, "patternProperties": { "[": {} } }""")]
    [TestCase("""{ "type": "object", "allOf": [{ "properties": { "x-extra": {} } }] }""")]
    [TestCase("""{ "type": "object", "properties": { "a": {} }, "anyOf": [{ "properties": { "x-extra": {} } }] }""")]
    [TestCase("""{ "type": "object", "$ref": "#/$defs/args", "$defs": { "args": { "properties": { "x-extra": {} } } } }""")]
    public void Schema_that_admits_extra_properties_accepts_unlisted_arguments(string json)
    {
        using JsonDocument schema = JsonDocument.Parse(json);

        CallToolResult? result = McpToolErrorFilters.CreateUnknownArgumentsResultOrNull(
            "extension_tool",
            ["a", "x-extra"],
            schema.RootElement);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Schema_that_forbids_extra_properties_rejects_unlisted_arguments()
    {
        using JsonDocument schema = JsonDocument.Parse(
            """{ "type": "object", "properties": { "a": {} }, "additionalProperties": false }""");

        CallToolResult? result = McpToolErrorFilters.CreateUnknownArgumentsResultOrNull(
            "extension_tool",
            ["a", "b"],
            schema.RootElement);

        Assert.That(ReadToolResult(result!).Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
    }

    [TestCase("""{ "type": "object", "properties": { "a": {} }, "patternProperties": { "^x-": {} } }""")]
    [TestCase("""{ "type": "object", "properties": { "a": {} }, "patternProperties": { "^x-": {} }, "additionalProperties": false }""")]
    public void Pattern_properties_accept_only_matching_names(string json)
    {
        using JsonDocument schema = JsonDocument.Parse(json);

        CallToolResult? result = McpToolErrorFilters.CreateUnknownArgumentsResultOrNull(
            "extension_tool",
            ["a", "x-extra", "y"],
            schema.RootElement);
        ToolResult<object?> toolResult = ReadToolResult(result!);

        Assert.Multiple(() =>
        {
            Assert.That(toolResult.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(toolResult.Error.Message, Does.Contain(": y."));
            Assert.That(toolResult.Error.Message, Does.Not.Contain("x-extra"));
            Assert.That(toolResult.Error.Message, Does.Contain("names matching ^x-"));
        });
    }

    private static ToolResult<object?> ReadToolResult(CallToolResult result)
    {
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return JsonSerializer.Deserialize<ToolResult<object?>>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Tool result JSON was empty.");
    }
}
