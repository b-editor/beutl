using System.Text.Json;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Mcp;

/// <summary>Carries one MCP tool call to <see cref="McpToolExtension.InvokeAsync"/>.</summary>
public sealed class McpToolCall
{
    public McpToolCall(
        string name,
        JsonElement arguments,
        IEditorContext? editorContext,
        IEditorContextServices services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(services);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool arguments must be a JSON object.", nameof(arguments));

        Name = name;
        Arguments = arguments;
        EditorContext = editorContext;
        Services = services;
    }

    /// <summary>The name of the called tool, as declared by <see cref="McpToolDefinition.Name"/>.</summary>
    public string Name { get; }

    /// <summary>The arguments the agent passed, as a JSON object.</summary>
    public JsonElement Arguments { get; }

    /// <summary>
    /// The editor of the tab selected in Beutl when the call started, or <see langword="null"/>
    /// when no editor is open.
    /// </summary>
    public IEditorContext? EditorContext { get; }

    public IEditorContextServices Services { get; }
}
