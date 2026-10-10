using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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

    // Raised after the set of tool names changed, on the thread that changed the extensions and
    // outside the catalog's lock.
    public event EventHandler? ToolsChanged;

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
        bool changed;
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

            Snapshot previous = _snapshot;
            Volatile.Write(ref _snapshot, new Snapshot(
                [.. tools],
                previous.RemovedNames
                    .Concat(previous.Tools.Select(tool => tool.ProtocolTool.Name))
                    .Where(name => !names.Contains(name))
                    .ToFrozenSet(StringComparer.Ordinal)));
            changed = !tools.Select(tool => tool.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(previous.Tools.Select(tool => tool.ProtocolTool.Name));
        }

        if (changed)
            ToolsChanged?.Invoke(this, EventArgs.Empty);
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

        if (definition.InputSchema.TryGetProperty("properties", out JsonElement properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("instanceId", out _))
        {
            // The instance router adds and consumes instanceId on every tool.
            s_logger.LogWarning(
                "Ignoring MCP tool {ToolName} from {ExtensionType}: the instanceId argument is reserved.",
                definition.Name,
                descriptor.TypeName);
            return null;
        }

        if (!names.Add(definition.Name))
        {
            s_logger.LogWarning(
                "Ignoring MCP tool {ToolName} from {ExtensionType}: another extension already provides it.",
                definition.Name,
                descriptor.TypeName);
            return null;
        }

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

    private sealed class Snapshot(ExtensionMcpTool[] tools, FrozenSet<string> removedNames)
    {
        public ExtensionMcpTool[] Tools { get; } = tools;

        // Names an extension provided earlier in this session that no loaded extension provides now.
        public FrozenSet<string> RemovedNames { get; } = removedNames;

        // Each request rebuilds the tool collection; report a collision once per snapshot, not per request.
        public ConcurrentDictionary<string, byte> ReportedConflicts { get; } = new(StringComparer.Ordinal);
    }
}
