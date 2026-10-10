using System.Text.Json;
using System.Text.RegularExpressions;
using Beutl.AgentToolkit.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Tools;

public static class McpToolErrorFilters
{
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan s_patternTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly string[] s_compositionKeywords =
        ["allOf", "anyOf", "oneOf", "if", "then", "else", "dependentSchemas", "$ref", "$dynamicRef"];

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
                return IsGenericInvocationError(result, context.Params?.Name) && IsAttributeDeclared(context)
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
        if (!TryGetAcceptedArguments(inputSchema, out HashSet<string>? accepted, out List<string>? patterns))
        {
            return null;
        }

        string[] unknown;
        try
        {
            // JSON Schema patterns follow ECMAScript, where \d and \w match only ASCII characters.
            unknown = argumentNames
                .Where(argument => !accepted.Contains(argument)
                                   && !patterns.Any(pattern => Regex.IsMatch(argument, pattern, RegexOptions.ECMAScript, s_patternTimeout)))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            // The tool owns its patterns; one this host cannot evaluate must not block the call.
            return null;
        }

        return unknown.Length == 0
            ? null
            : CreateUnknownArgumentsResult(toolName, unknown, accepted, patterns);
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

    private static bool TryGetAcceptedArguments(
        JsonElement inputSchema,
        out HashSet<string> accepted,
        out List<string> patterns)
    {
        accepted = new HashSet<string>(StringComparer.Ordinal);
        patterns = [];
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // A schema that admits arguments beyond its named properties has no fixed name set to enforce.
        if (inputSchema.TryGetProperty("additionalProperties", out JsonElement additional)
            && additional.ValueKind != JsonValueKind.False)
        {
            return false;
        }

        // Names declared by subschemas or references are not resolved here, so they cannot be rejected.
        if (s_compositionKeywords.Any(keyword => inputSchema.TryGetProperty(keyword, out _)))
        {
            return false;
        }

        if (inputSchema.TryGetProperty("patternProperties", out JsonElement patternProperties))
        {
            if (patternProperties.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (JsonProperty pattern in patternProperties.EnumerateObject())
            {
                patterns.Add(pattern.Name);
            }
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

    // Tools built from other sources, such as extensions, may return this text as ordinary output.
    private static bool IsAttributeDeclared(RequestContext<CallToolRequestParams> context)
    {
        return ResolveTool(context) is not { } tool
               || tool.Metadata.OfType<McpServerToolAttribute>().Any();
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
        string target = ResolveTarget(toolName);
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

        return CreateTextResult(result);
    }

    private static CallToolResult CreateUnknownArgumentsResult(
        string? toolName,
        IReadOnlyList<string> unknown,
        IReadOnlySet<string> accepted,
        IReadOnlyList<string> patterns)
    {
        string target = ResolveTarget(toolName);
        string unknownList = string.Join(", ", unknown);
        List<string> acceptedParts = [.. accepted.Order(StringComparer.Ordinal)];
        if (patterns.Count > 0)
        {
            acceptedParts.Add($"names matching {string.Join(" or ", patterns)}");
        }

        string acceptedList = acceptedParts.Count == 0 ? "none" : string.Join(", ", acceptedParts);
        ToolResult<object?> result = ToolResult<object?>.Failure(
            ErrorCode.ValidationRejected,
            $"Unknown argument(s) for '{target}': {unknownList}. Accepted parameters: {acceptedList}.",
            target,
            "Remove unknown arguments, or call tools/list for the current schema and pass only documented argument names.");

        return CreateTextResult(result);
    }

    private static string ResolveTarget(string? toolName)
    {
        return string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
    }

    private static CallToolResult CreateTextResult(ToolResult<object?> result)
    {
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
