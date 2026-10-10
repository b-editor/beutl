using System.Text.Json;

namespace Beutl.Editor.Services.Mcp;

/// <summary>The result of one MCP tool call.</summary>
public sealed class McpToolResult
{
    /// <param name="content">The content blocks returned to the agent.</param>
    /// <param name="structuredContent">An optional JSON object that clients can parse directly.</param>
    /// <param name="isError">Whether the call failed in a way the agent should see and act on.</param>
    public McpToolResult(
        IEnumerable<McpToolContent> content,
        JsonElement? structuredContent = null,
        bool isError = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        McpToolContent[] blocks = content.ToArray();
        if (blocks.Any(block => block is null))
            throw new ArgumentException("Content cannot contain null.", nameof(content));
        if (structuredContent is { ValueKind: not JsonValueKind.Object })
        {
            throw new ArgumentException(
                "Structured content must be a JSON object.",
                nameof(structuredContent));
        }

        Content = Array.AsReadOnly(blocks);
        StructuredContent = structuredContent?.Clone();
        IsError = isError;
    }

    public IReadOnlyList<McpToolContent> Content { get; }

    public JsonElement? StructuredContent { get; }

    public bool IsError { get; }

    public static McpToolResult Text(string text) => new([new McpTextContent(text)]);

    /// <summary>
    /// Returns <paramref name="value"/> as text and, when it is a JSON object, also as structured
    /// content.
    /// </summary>
    public static McpToolResult Json(JsonElement value)
        => new(
            [new McpTextContent(value.GetRawText())],
            value.ValueKind == JsonValueKind.Object ? value : null);

    public static McpToolResult Image(ReadOnlyMemory<byte> data, string mimeType)
        => new([new McpImageContent(data, mimeType)]);

    public static McpToolResult Error(string message) => new([new McpTextContent(message)], isError: true);
}

/// <summary>One content block of an <see cref="McpToolResult"/>.</summary>
public abstract class McpToolContent
{
    private protected McpToolContent()
    {
    }
}

public sealed class McpTextContent : McpToolContent
{
    public McpTextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed class McpImageContent : McpToolContent
{
    /// <param name="data">The encoded image bytes.</param>
    /// <param name="mimeType">The image media type, such as <c>image/png</c>.</param>
    public McpImageContent(ReadOnlyMemory<byte> data, string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (data.IsEmpty)
            throw new ArgumentException("Image data cannot be empty.", nameof(data));

        Data = data.ToArray();
        MimeType = mimeType;
    }

    public ReadOnlyMemory<byte> Data { get; }

    public string MimeType { get; }
}
