using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Beutl.AgentToolkit.Tools;

public static class McpToolErrorFilters
{
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);

    public static IMcpRequestFilterBuilder AddToolkitCallToolErrorFilter(this IMcpRequestFilterBuilder filters)
    {
        return filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
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
        string message = detail is null
            ? $"Tool arguments could not be bound for '{target}'. Check required parameters and parameter types."
            : $"Tool arguments could not be bound for '{target}': {detail}";
        ToolResult<object?> result = ToolResult<object?>.Failure(
            ErrorCode.ValidationRejected,
            message,
            target,
            "Call tools/list for the current schema and pass the required arguments with the documented JSON shapes.");

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
