using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public static class McpToolErrorFilters
{
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);

    public static IMcpRequestFilterBuilder AddToolkitCallToolErrorFilter(this IMcpRequestFilterBuilder filters)
    {
        return filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            CallToolResult? unknownArgumentsResult = CreateUnknownArgumentsResultOrNull(context);
            if (unknownArgumentsResult is not null)
            {
                return unknownArgumentsResult;
            }

            try
            {
                CallToolResult result = await next(context, cancellationToken).ConfigureAwait(false);
                return IsGenericInvocationError(result, context.Params?.Name)
                    ? CreateValidationRejectedResult(context.Params?.Name)
                    : result;
            }
            catch (Exception ex) when (IsBindingException(ex))
            {
                return CreateValidationRejectedResult(context.Params?.Name, ex.Message);
            }
        });
    }

    private static CallToolResult? CreateUnknownArgumentsResultOrNull(
        RequestContext<CallToolRequestParams> context)
    {
        if (context.Params?.Arguments is not { Count: > 0 } arguments)
        {
            return null;
        }

        McpServerTool? tool = ResolveTool(context);
        if (tool is null)
        {
            return null;
        }

        return CreateUnknownArgumentsResultOrNull(context.Params.Name, arguments.Keys, tool.ProtocolTool.InputSchema);
    }

    internal static CallToolResult? CreateUnknownArgumentsResultOrNull(
        string? toolName,
        IEnumerable<string> argumentNames,
        JsonElement inputSchema)
    {
        if (!TryGetAcceptedArgumentNames(inputSchema, out HashSet<string>? accepted))
        {
            return null;
        }

        string[] unknown = argumentNames
            .Where(argument => !accepted.Contains(argument))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return unknown.Length == 0
            ? null
            : CreateUnknownArgumentsResult(toolName, unknown, accepted);
    }

    private static McpServerTool? ResolveTool(RequestContext<CallToolRequestParams> context)
    {
        if (context.MatchedPrimitive is McpServerTool matched)
        {
            return matched;
        }

        string? toolName = context.Params?.Name;
        if (string.IsNullOrWhiteSpace(toolName)
            || context.Server?.ServerOptions.ToolCollection is not { } tools)
        {
            return null;
        }

        return tools.TryGetPrimitive(toolName, out McpServerTool? tool) ? tool : null;
    }

    private static bool TryGetAcceptedArgumentNames(JsonElement inputSchema, out HashSet<string> accepted)
    {
        accepted = new HashSet<string>(StringComparer.Ordinal);
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!inputSchema.TryGetProperty("properties", out JsonElement properties))
        {
            return true;
        }

        if (properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in properties.EnumerateObject())
        {
            accepted.Add(property.Name);
        }

        return true;
    }

    private static bool IsBindingException(Exception ex)
    {
        return ex is ArgumentException or JsonException or NotSupportedException or FormatException;
    }

    private static bool IsGenericInvocationError(CallToolResult result, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        string expected = $"An error occurred invoking '{toolName}'.";
        return result.Content
            .OfType<TextContentBlock>()
            .Any(block => string.Equals(block.Text, expected, StringComparison.Ordinal));
    }

    private static CallToolResult CreateValidationRejectedResult(string? toolName, string? detail = null)
    {
        string target = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
        // This filter wraps the whole downstream pipeline, so it cannot tell argument binding, the
        // tool body, and result serialization apart. Do not name a cause the caller cannot trust.
        string message = detail is null
            ? $"Call to '{target}' failed."
            : $"Call to '{target}' failed: {detail}";
        ToolResult<object?> result = ToolResult<object?>.Failure(
            ErrorCode.ValidationRejected,
            message,
            target,
            "If the message names a missing or mistyped argument, call tools/list for the current schema and pass the documented JSON shapes. Otherwise the tool may have already applied its changes, so read back the current state before retrying to avoid applying the same edit twice.");

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(result, s_toolResultOptions)
                }
            ],
            IsError = false
        };
    }

    private static CallToolResult CreateUnknownArgumentsResult(
        string? toolName,
        IReadOnlyList<string> unknown,
        IReadOnlySet<string> accepted)
    {
        string target = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
        string unknownList = string.Join(", ", unknown);
        string acceptedList = accepted.Count == 0
            ? "none"
            : string.Join(", ", accepted.Order(StringComparer.Ordinal));
        ToolResult<object?> result = ToolResult<object?>.Failure(
            ErrorCode.ValidationRejected,
            $"Unknown argument(s) for '{target}': {unknownList}. Accepted parameters: {acceptedList}.",
            target,
            "Remove unknown arguments, or call tools/list for the current schema and pass only documented argument names.");

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(result, s_toolResultOptions)
                }
            ],
            IsError = false
        };
    }
}
