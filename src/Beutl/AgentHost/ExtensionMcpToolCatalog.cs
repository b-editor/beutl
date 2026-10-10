using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beutl.Editor.Services.Mcp;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentHost;

// Mirrors McpToolExtension contributions as MCP tools. The endpoint serves each stateless request
// from the snapshot current at that time, so installed or removed packages need no restart.
internal sealed class ExtensionMcpToolCatalog : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ExtensionMcpToolCatalog>();
    private readonly EditorService _editorService;
    private readonly IEditorContextServices _services;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
    private Snapshot _snapshot = new([], FrozenSet<string>.Empty);
    private bool _disposed;

    public ExtensionMcpToolCatalog(EditorService editorService)
    {
        _editorService = editorService;
        _services = new EditorContextServices(editorService, editorService.ExtensionProvider);
        editorService.ExtensionProvider.ExtensionsChanged += OnExtensionsChanged;
        Rebuild();
    }

    public IReadOnlyList<ExtensionMcpTool> Tools => Volatile.Read(ref _snapshot).Tools;

    // Built-in tools are already in the collection, so they win a name collision.
    public void AddTo(McpServerOptions options)
    {
        Snapshot snapshot = Volatile.Read(ref _snapshot);
        if (snapshot.Tools.Length == 0)
            return;

        McpServerPrimitiveCollection<McpServerTool> tools = options.ToolCollection ??= [];
        foreach (ExtensionMcpTool tool in snapshot.Tools)
        {
            if (!tools.TryAdd(tool) && snapshot.ReportedConflicts.TryAdd(tool.ProtocolTool.Name, 0))
            {
                s_logger.LogWarning(
                    "Ignoring MCP tool {ToolName} from {ExtensionType}: a built-in tool has the same name.",
                    tool.ProtocolTool.Name,
                    tool.ExtensionType);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _editorService.ExtensionProvider.ExtensionsChanged -= OnExtensionsChanged;
            Volatile.Write(ref _snapshot, new Snapshot([], FrozenSet<string>.Empty));
        }
    }

    private void OnExtensionsChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        // Serialized so a rebuild that started before a change cannot publish after the rebuild for it.
        lock (_gate)
        {
            if (_disposed)
                return;

            var tools = new List<ExtensionMcpTool>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            IExtensionProvider extensions = _editorService.ExtensionProvider;
            foreach (ExtensionDescriptor descriptor in extensions.GetDescriptors<McpToolExtension>())
            {
                if (!extensions.TryAcquire(descriptor.Id, out IExtensionLease<McpToolExtension>? lease))
                    continue;

                using (lease)
                {
                    McpToolDefinition?[] definitions;
                    try
                    {
                        definitions = lease.Extension.Tools?.ToArray() ?? [];
                    }
                    catch (Exception ex)
                    {
                        s_logger.LogWarning(ex, "Ignoring the MCP tools of {ExtensionType}.", descriptor.TypeName);
                        continue;
                    }

                    foreach (McpToolDefinition? definition in definitions)
                    {
                        if (CreateTool(descriptor, definition, names) is { } tool)
                            tools.Add(tool);
                    }
                }
            }

            Volatile.Write(ref _snapshot, new Snapshot(
                [.. tools],
                _owners.Keys.Where(name => !names.Contains(name)).ToFrozenSet(StringComparer.Ordinal)));
        }
    }

    // A client may call a tool it listed before the package was removed. That request no longer
    // contains the tool, so answer it here instead of with the SDK's unknown-tool protocol error.
    public bool TryCreateRemovedToolResult(
        RequestContext<CallToolRequestParams> context,
        [NotNullWhen(true)] out CallToolResult? result)
    {
        string? name = context.Params?.Name;
        if (name is null
            || !Volatile.Read(ref _snapshot).RemovedNames.Contains(name)
            || context.Server.ServerOptions.ToolCollection?.TryGetPrimitive(name, out _) == true)
        {
            result = null;
            return false;
        }

        result = ExtensionMcpTool.CreateUnavailableResult(name);
        return true;
    }

    private ExtensionMcpTool? CreateTool(
        ExtensionDescriptor descriptor,
        McpToolDefinition? definition,
        HashSet<string> names)
    {
        if (definition is null)
        {
            s_logger.LogWarning("Ignoring a null MCP tool definition from {ExtensionType}.", descriptor.TypeName);
            return null;
        }

        // The instance router adds and consumes instanceId on every tool.
        switch (FindInstanceIdUse(definition.InputSchema, definition.InputSchema, depth: 0))
        {
            case InstanceIdUse.Constrained:
                s_logger.LogWarning(
                    "Ignoring MCP tool {ToolName} from {ExtensionType}: the instanceId argument is reserved.",
                    definition.Name,
                    descriptor.TypeName);
                return null;
            case InstanceIdUse.Unresolved:
                s_logger.LogWarning(
                    "Ignoring MCP tool {ToolName} from {ExtensionType}: its input schema references a schema outside itself.",
                    definition.Name,
                    descriptor.TypeName);
                return null;
        }

        // A client may still call a name it listed earlier, so a name never moves to another extension
        // within a session. An updated version of the same extension keeps its names.
        string identity = GetExtensionIdentity(descriptor.TypeName);
        if (_owners.TryGetValue(definition.Name, out string? owner) && owner != identity)
        {
            s_logger.LogWarning(
                "Ignoring MCP tool {ToolName} from {ExtensionType}: {Owner} provided it earlier in this session.",
                definition.Name,
                descriptor.TypeName,
                owner);
            return null;
        }

        if (!names.Add(definition.Name))
        {
            s_logger.LogWarning(
                "Ignoring MCP tool {ToolName} from {ExtensionType}: it is declared more than once.",
                definition.Name,
                descriptor.TypeName);
            return null;
        }

        _owners[definition.Name] = identity;

        var protocolTool = new Tool
        {
            Name = definition.Name,
            Title = definition.Title,
            Description = definition.Description,
            InputSchema = definition.InputSchema.Clone(),
            Annotations = definition is { ReadOnlyHint: null, DestructiveHint: null, IdempotentHint: null, OpenWorldHint: null }
                ? null
                : new ToolAnnotations
                {
                    ReadOnlyHint = definition.ReadOnlyHint,
                    DestructiveHint = definition.DestructiveHint,
                    IdempotentHint = definition.IdempotentHint,
                    OpenWorldHint = definition.OpenWorldHint
                }
        };
        return new ExtensionMcpTool(descriptor.Id, descriptor.TypeName, protocolTool, _editorService, _services);
    }

    // Looks through every subschema that applies to the arguments object, following local references.
    private static InstanceIdUse FindInstanceIdUse(JsonElement root, JsonElement schema, int depth)
    {
        const string name = "instanceId";
        if (schema.ValueKind != JsonValueKind.Object)
            return InstanceIdUse.None;
        if (depth > 32)
            return InstanceIdUse.Unresolved;

        if (schema.TryGetProperty("properties", out JsonElement properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(name, out _))
        {
            return InstanceIdUse.Constrained;
        }

        if (schema.TryGetProperty("required", out JsonElement required)
            && required.ValueKind == JsonValueKind.Array
            && required.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.ValueEquals(name)))
        {
            return InstanceIdUse.Constrained;
        }

        if (schema.TryGetProperty("patternProperties", out JsonElement patterns)
            && patterns.ValueKind == JsonValueKind.Object
            && patterns.EnumerateObject().Any(pattern => MatchesPattern(pattern.Name, name)))
        {
            return InstanceIdUse.Constrained;
        }

        if (schema.TryGetProperty("dependentSchemas", out JsonElement dependents)
            && dependents.ValueKind == JsonValueKind.Object
            && dependents.TryGetProperty(name, out _))
        {
            return InstanceIdUse.Constrained;
        }

        foreach (JsonElement subschema in GetArgumentSubschemas(root, schema))
        {
            InstanceIdUse use = subschema.ValueKind == JsonValueKind.Undefined
                ? InstanceIdUse.Unresolved
                : FindInstanceIdUse(root, subschema, depth + 1);
            if (use != InstanceIdUse.None)
                return use;
        }

        return InstanceIdUse.None;
    }

    // Yields default(JsonElement) for a reference that does not resolve inside the schema.
    private static IEnumerable<JsonElement> GetArgumentSubschemas(JsonElement root, JsonElement schema)
    {
        foreach (string keyword in new[] { "$ref", "$dynamicRef" })
        {
            if (schema.TryGetProperty(keyword, out JsonElement reference))
                yield return reference.ValueKind == JsonValueKind.String ? ResolveLocalReference(root, reference.GetString()!) : default;
        }

        foreach (string keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(keyword, out JsonElement branches) && branches.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement branch in branches.EnumerateArray())
                    yield return branch;
            }
        }

        foreach (string keyword in new[] { "if", "then", "else" })
        {
            if (schema.TryGetProperty(keyword, out JsonElement branch))
                yield return branch;
        }

        if (schema.TryGetProperty("dependentSchemas", out JsonElement dependents) && dependents.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty dependent in dependents.EnumerateObject())
                yield return dependent.Value;
        }
    }

    private static JsonElement ResolveLocalReference(JsonElement root, string reference)
    {
        if (reference == "#")
            return root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            return default;

        JsonElement current = root;
        foreach (string encoded in reference[2..].Split('/'))
        {
            string segment = Uri.UnescapeDataString(encoded).Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out JsonElement child))
                current = child;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out int index)
                     && index >= 0 && index < current.GetArrayLength())
                current = current[index];
            else
                return default;
        }

        return current;
    }

    private static bool MatchesPattern(string pattern, string name)
    {
        try
        {
            return Regex.IsMatch(name, pattern, RegexOptions.ECMAScript, TimeSpan.FromMilliseconds(100));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string GetExtensionIdentity(string typeName)
        => System.Reflection.Metadata.TypeName.TryParse(typeName, out System.Reflection.Metadata.TypeName? parsed)
            ? $"{parsed.FullName}, {parsed.AssemblyName?.Name}"
            : typeName;

    private enum InstanceIdUse
    {
        None,
        Constrained,
        Unresolved
    }

    private sealed class Snapshot(ExtensionMcpTool[] tools, FrozenSet<string> removedNames)
    {
        public ExtensionMcpTool[] Tools { get; } = tools;

        // Names an extension provided earlier in this session that no loaded extension provides now.
        public FrozenSet<string> RemovedNames { get; } = removedNames;

        // Each request rebuilds the tool collection; report a collision once per snapshot, not per request.
        public ConcurrentDictionary<string, byte> ReportedConflicts { get; } = new(StringComparer.Ordinal);
    }
}
