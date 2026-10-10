# MCP tool extension guide

`McpToolExtension` adds tools to the live MCP endpoint that each running Beutl hosts for AI agents. Agents reach it through the `beutl-agent` MCP server installed from the AI Agents settings page, which forwards every call that names an `instanceId` to that instance. The public contract lives in `src/Beutl.Editor/Services/Mcp/`; the host side is `src/Beutl/AgentHost/ExtensionMcpToolCatalog.cs` and `ExtensionMcpTool.cs`.

Extension tools run only inside the in-app endpoint. The installed server (`Beutl.AgentToolkit.Mcp`) does not load extensions itself: it lists them while an editor runs, requires `instanceId` on them, and rejects a call without one instead of running it headlessly.

## Defining tools

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor.Services.Mcp;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

[Export]
public sealed class SceneInfoToolExtension : McpToolExtension
{
    public override IReadOnlyList<McpToolDefinition> Tools { get; } =
    [
        new McpToolDefinition(
            "myext.count_elements",
            "Counts the elements on one layer of the scene open in the Beutl editor.",
            JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": { "layer": { "type": "integer", "description": "Zero-based layer index." } },
                  "required": ["layer"]
                }
                """).RootElement)
        {
            ReadOnlyHint = true
        }
    ];

    public override ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken)
    {
        if (call.EditorContext?.GetService(typeof(Scene)) is not Scene scene)
            return new(McpToolResult.Error("Open a scene in the Beutl editor first."));

        int layer = call.Arguments.GetProperty("layer").GetInt32();
        int count = scene.Children.Count(element => element.ZIndex == layer);
        var value = new JsonObject { ["layer"] = layer, ["count"] = count };
        return new(McpToolResult.Json(JsonSerializer.SerializeToElement(value)));
    }
}
```

`Tools` is read when the extension is registered and again whenever the set of loaded extensions changes, possibly from a background thread; the host copies the metadata, so return the same definitions every time and do not touch UI state there. One extension can declare several tools and dispatch on `McpToolCall.Name`.

## Names and schemas

- A tool name is 1 to 128 ASCII letters, digits, `_`, `-`, or `.`; `McpToolDefinition` throws otherwise.
- Built-in tools and every extension share one namespace. A name that matches a built-in tool, or a tool that an earlier extension already registered, is skipped and logged. Prefix names with something specific to your extension.
- The input schema must be a JSON Schema object with `"type": "object"`. Omit it for a tool without arguments.
- Arguments that are neither in `properties` nor matched by a `patternProperties` pattern are rejected with `validation_rejected` before your code runs, as for built-in tools. Patterns use ECMAScript regular expressions, as in JSON Schema. Set `additionalProperties` to anything but `false` to accept any argument name. Schemas that combine subschemas at the top level (`allOf`, `anyOf`, `oneOf`, `if`/`then`/`else`, `dependentSchemas`) or use `$ref` there are not checked, because the host does not resolve them.
- `instanceId` is reserved: Beutl adds it to every tool to route calls between running Beutl instances, and removes it before your code sees the arguments. A tool that declares an `instanceId` property is skipped.
- `tools/list` shows the extension tools loaded in the instance the agent is connected to; through the installed server it describes them from one running instance and announces changes with `notifications/tools/list_changed` as editors start and exit and as packages add or remove tools in them. Instances that share a profile load the same packages at startup, but a package installed or removed while several instances run is listed only by the instances where it is loaded, and a call routed with `instanceId` to an instance without the tool fails.
- `ReadOnlyHint`, `DestructiveHint`, `IdempotentHint`, and `OpenWorldHint` become MCP tool annotations, which clients use when deciding whether to ask the user before a call.

## Running a call

- `InvokeAsync` runs on the UI thread, so editor objects can be read and changed directly. Move CPU-heavy or blocking work to a background thread.
- `McpToolCall.EditorContext` is the editor of the tab that was selected when the call started, or `null` when no editor is open. Resolve editor services through it, for example `Scene` and `HistoryManager`. Commit edits with `HistoryManager.Commit` so the user can undo them as one step.
- The call leases your extension, not the editor. If the user closes the tab while your code awaits, the editor is disposed and `GetService(typeof(Scene))` returns `null` from then on. Do the editor work before the first await, or resolve the scene again after each await and stop when it is `null`; do not keep using objects you resolved before the await.
- `McpToolCall.Services` gives access to the extension provider and host services, as `IEditorContextServices` does for editor extensions.
- Honor the `CancellationToken`; it is canceled when the agent cancels the request.

## Results and errors

- `McpToolResult.Text`, `Json`, and `Image` build common results. `Json` also returns a JSON object as structured content. Construct `McpToolResult` directly to return several content blocks.
- Return `McpToolResult.Error(message)` for failures the agent can act on, such as a missing editor or an out-of-range argument. The message is shown to the agent as is.
- An exception becomes an `extension_tool_failed` error that carries the exception message, and is logged.
- A call to a tool whose package was unloaded after the agent listed it returns `extension_tool_unavailable`.
- Both errors are marked as MCP tool errors (`isError: true`), like `McpToolResult.Error`.

## Lifetime

Tools appear and disappear with their package without restarting the endpoint. A running call keeps its extension leased, so uninstalling waits until the call finishes. `McpToolExtension` supports live unload; a package whose extensions all support it can be removed without restarting Beutl.
