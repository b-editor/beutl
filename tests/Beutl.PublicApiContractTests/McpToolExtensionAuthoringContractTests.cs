using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Beutl.Editor.Services.Mcp;
using Beutl.Extensibility;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class McpToolExtensionAuthoringContractTests : PublicApiContractTestBase
{
    [Test]
    public async Task APluginAuthoredExtension_CanDeclareAndRunTools()
    {
        AssertDoesNotHaveFriendAccess(typeof(McpToolExtension).Assembly);
        var extension = new PluginToolExtension();
        using JsonDocument arguments = JsonDocument.Parse("""{"text":"hello"}""");

        McpToolResult result = await extension.InvokeAsync(
            new McpToolCall("vendor.upper", arguments.RootElement, null, new Services()),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            McpToolDefinition tool = extension.Tools.Single();
            Assert.That(tool.Name, Is.EqualTo("vendor.upper"));
            Assert.That(tool.InputSchema.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(tool.ReadOnlyHint, Is.True);
            Assert.That(result.IsError, Is.False);
            Assert.That(result.StructuredContent?.GetProperty("text").GetString(), Is.EqualTo("HELLO"));
            Assert.That(((McpTextContent)result.Content.Single()).Text, Is.EqualTo("""{"text":"HELLO"}"""));
        }
    }

    [TestCase("")]
    [TestCase("has space")]
    [TestCase("ツール")]
    [TestCase("slash/name")]
    public void ToolNames_OutsideTheMcpCharacterSet_AreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => _ = new McpToolDefinition(name, "Description."));
    }

    [Test]
    public void ToolNames_AreLimitedTo128Characters()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new McpToolDefinition(new string('a', 128), "Description.").Name, Has.Length.EqualTo(128));
            Assert.Throws<ArgumentException>(() => _ = new McpToolDefinition(new string('a', 129), "Description."));
        }
    }

    [TestCase("""{"type":"string"}""")]
    [TestCase("""{"properties":{}}""")]
    [TestCase("""[]""")]
    public void InputSchemas_MustDescribeAnObject(string json)
    {
        using JsonDocument schema = JsonDocument.Parse(json);

        Assert.Throws<ArgumentException>(() => _ = new McpToolDefinition("vendor.tool", "Description.", schema.RootElement));
    }

    [Test]
    public void Definitions_KeepTheirSchemaAfterTheSourceDocumentIsDisposed()
    {
        McpToolDefinition definition;
        using (JsonDocument schema = JsonDocument.Parse("""{"type":"object","properties":{"a":{}}}"""))
            definition = new McpToolDefinition("vendor.tool", "Description.", schema.RootElement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.InputSchema.GetProperty("properties").TryGetProperty("a", out _), Is.True);
            Assert.That(
                new McpToolDefinition("vendor.empty", "Description.").InputSchema.GetProperty("type").GetString(),
                Is.EqualTo("object"));
        }
    }

    [Test]
    public void Results_DescribeTextImagesAndErrors()
    {
        McpToolResult image = McpToolResult.Image(new byte[] { 1, 2, 3 }, "image/png");
        McpToolResult error = McpToolResult.Error("bad input");
        McpToolResult array = McpToolResult.Json(JsonSerializer.SerializeToElement(new[] { 1, 2 }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((McpImageContent)image.Content.Single()).MimeType, Is.EqualTo("image/png"));
            Assert.That(((McpImageContent)image.Content.Single()).Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(error.IsError, Is.True);
            Assert.That(((McpTextContent)error.Content.Single()).Text, Is.EqualTo("bad input"));
            Assert.That(array.StructuredContent, Is.Null);
            Assert.That(((McpTextContent)array.Content.Single()).Text, Is.EqualTo("[1,2]"));
            Assert.Throws<ArgumentException>(() => _ = new McpToolResult(
                [], JsonSerializer.SerializeToElement(1)));
        }
    }

    [Test]
    public void Calls_RequireObjectArguments()
    {
        Assert.Throws<ArgumentException>(() => _ = new McpToolCall(
            "vendor.tool", JsonSerializer.SerializeToElement(1), null, new Services()));
    }

    private sealed class PluginToolExtension : McpToolExtension
    {
        public override IReadOnlyList<McpToolDefinition> Tools { get; } =
        [
            new McpToolDefinition(
                "vendor.upper",
                "Converts text to upper case.",
                JsonDocument.Parse("""{"type":"object","properties":{"text":{"type":"string"}}}""").RootElement)
            {
                ReadOnlyHint = true
            }
        ];

        public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
        {
            string text = call.Arguments.GetProperty("text").GetString()!.ToUpperInvariant();
            return new(McpToolResult.Json(JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["text"] = text })));
        }
    }

    private sealed class Services : IEditorContextServices
    {
        public IExtensionProvider ExtensionProvider => throw new NotSupportedException();

        public bool TryGetService<T>([NotNullWhen(true)] out T? service)
            where T : class
        {
            service = null;
            return false;
        }
    }
}
