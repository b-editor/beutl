using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.Editor.Services.Mcp;

/// <summary>
/// Contributes tools to the live MCP endpoint that Beutl hosts for AI agents.
/// </summary>
/// <remarks>
/// <para>
/// The host reads <see cref="Tools"/> whenever the set of loaded extensions changes, possibly from a
/// background thread, and copies the metadata, so return the same definitions on every read without
/// touching UI state. Tool names share one namespace with Beutl's built-in
/// tools and with other extensions; a colliding name is skipped and logged, so prefix names with
/// something specific to the extension.
/// </para>
/// <para>
/// <see cref="InvokeAsync"/> runs on the UI thread, so editor objects reachable from
/// <see cref="McpToolCall.EditorContext"/> can be used directly. Move long-running work off the UI
/// thread. The package stays loaded until every running call completes, which lets a package that
/// contains only live-unloadable extensions be uninstalled without a restart.
/// </para>
/// </remarks>
public abstract class McpToolExtension : Extension, ILiveUnloadExtension
{
    public abstract IReadOnlyList<McpToolDefinition> Tools { get; }

    /// <summary>Runs the tool named by <see cref="McpToolCall.Name"/>.</summary>
    /// <remarks>
    /// Report failures that the agent can act on with <see cref="McpToolResult.Error(string)"/>.
    /// An exception is reported to the agent as a failed call with the exception message.
    /// </remarks>
    public abstract ValueTask<McpToolResult> InvokeAsync(McpToolCall call, CancellationToken cancellationToken);
}
