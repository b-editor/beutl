using System.Buffers;
using System.Text.Json;
using Avalonia.Threading;
using Beutl.AgentToolkit.Common;
using Beutl.Editor.Services.Mcp;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

// Holds only copied metadata and the extension ID. Each call leases the extension, so a removed
// package is never rooted by the endpoint and unloads only after its running calls finish.
internal sealed class ExtensionMcpTool(
    ExtensionId extensionId,
    string extensionType,
    Tool protocolTool,
    EditorService editorService,
    IEditorContextServices services) : McpServerTool
{
    private static readonly ILogger s_logger = Log.CreateLogger<ExtensionMcpTool>();
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public string ExtensionType => extensionType;

    public override Tool ProtocolTool => protocolTool;

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        if (!editorService.ExtensionProvider.TryAcquire(extensionId, out IExtensionLease<McpToolExtension>? lease))
            return CreateUnavailableResult(protocolTool.Name);

        using (lease)
        {
            try
            {
                JsonElement arguments = CreateArguments(request.Params?.Arguments);
                Task<McpToolResult> invocation = await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        var call = new McpToolCall(
                            protocolTool.Name,
                            arguments,
                            editorService.SelectedTabItem.Value?.Context.Value,
                            services);
                        return lease.Extension.InvokeAsync(call, cancellationToken).AsTask();
                    },
                    DispatcherPriority.Normal,
                    cancellationToken);
                McpToolResult? result = await invocation.ConfigureAwait(false);
                return result is null
                    ? Failed("The extension returned no result.")
                    : ToProtocolResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "MCP tool {ToolName} from {ExtensionType} failed.", protocolTool.Name, extensionType);
                return Failed(ex.Message);
            }
        }
    }

    private static JsonElement CreateArguments(IDictionary<string, JsonElement>? arguments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach ((string name, JsonElement value) in arguments ?? new Dictionary<string, JsonElement>())
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static CallToolResult ToProtocolResult(McpToolResult result)
    {
        var content = new List<ContentBlock>(result.Content.Count);
        foreach (McpToolContent block in result.Content)
        {
            content.Add(block switch
            {
                McpTextContent text => new TextContentBlock { Text = text.Text },
                McpImageContent image => ImageContentBlock.FromBytes(image.Data, image.MimeType),
                _ => throw new NotSupportedException($"Unsupported MCP tool content '{block.GetType().Name}'.")
            });
        }

        return new CallToolResult
        {
            Content = content,
            StructuredContent = result.StructuredContent,
            IsError = result.IsError
        };
    }

    public static CallToolResult CreateUnavailableResult(string toolName)
        => Error(
            toolName,
            ErrorCode.ExtensionToolUnavailable,
            $"The extension that provided '{toolName}' is no longer loaded.",
            "Call tools/list for the tools that are currently available.");

    private CallToolResult Failed(string message)
        => Error(
            protocolTool.Name,
            ErrorCode.ExtensionToolFailed,
            $"'{protocolTool.Name}' failed: {message}",
            "The tool may have applied part of its work. Read back the current state before retrying.");

    private static CallToolResult Error(string toolName, string code, string message, string hint)
        => new()
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(
                        ToolResult<object?>.Failure(code, message, toolName, hint),
                        s_jsonOptions)
                }
            ],
            IsError = true
        };
}
