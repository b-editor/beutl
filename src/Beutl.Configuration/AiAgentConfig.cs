using System.ComponentModel;

using System.Reactive;
using Beutl.Serialization;

namespace Beutl.Configuration;

// #2852 removed the agent workspace root (agent tools take absolute paths), so a stored WorkspaceRoot is dropped
// rather than kept as an unknown member.
[DroppedMembers("WorkspaceRoot")]
public sealed class AiAgentConfig : ConfigurationBase
{
    public AiAgentConfig()
    {
        ScanProperties<AiAgentConfig>();
    }

    // Empty means "use the host-computed default" (first catalog agent, project
    // scope, or the selected agent's own conventions).
    public IProperty<string> AgentId { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> InstallScope { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> ProjectRoot { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> SkillsDirectory { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> SubagentsDirectory { get; } = new ValueOnlyProperty<string>("");

    public IProperty<bool> InstallSkills { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> InstallSubagents { get; } = new ValueOnlyProperty<bool>(true);

    // One MCP server covers live editing of running editors and headless file editing.
    public IProperty<bool> InstallMcp { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<string> McpConfigFileName { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> McpServersPropertyName { get; } = new ValueOnlyProperty<string>("");

    // Keep legacy settings until migration succeeds, then omit this property from new saves.
    public IProperty<string> LiveMcpToken { get; } = new ValueOnlyProperty<string>("");

    // Empty means "use the detected stdio launcher"; a non-empty value is the user's override, which must
    // survive reopening the settings page.
    public IProperty<string> StdioCommand { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> StdioArguments { get; } = new ValueOnlyProperty<string>("");

    // Experimental: move the editor to each live MCP edit and rendered frame. Off by default because it
    // takes over the selection, playhead and scroll position.
    public IProperty<bool> FollowLiveMcpEdits { get; } = new ValueOnlyProperty<bool>(false);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }

    // Property names of the separate MCP options of earlier versions, read once for migration.
    private const string LegacyInstallStdioMcp = "InstallStdioMcp";
    private const string LegacyInstallLiveMcp = "InstallLiveMcp";

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        // Earlier versions had separate stdio and live MCP options. A profile that turned both off
        // opted out of MCP; keep that choice instead of re-enabling the unified option.
        if (!context.Contains(nameof(InstallMcp))
            && context.Contains(LegacyInstallStdioMcp) && context.Contains(LegacyInstallLiveMcp)
            && TryReadValue(context, LegacyInstallStdioMcp, out bool stdio) && !stdio
            && TryReadValue(context, LegacyInstallLiveMcp, out bool live) && !live)
        {
            InstallMcp.Value = false;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        // A settings save (including startup failure recovery) must not erase the only copy
        // of a legacy credential before the endpoint has durably migrated it.
        if (string.IsNullOrWhiteSpace(LiveMcpToken.Value))
            context.SetValue(nameof(LiveMcpToken), Unit.Default);
    }
}
