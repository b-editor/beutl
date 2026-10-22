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

    public IProperty<string> AgentIdProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> InstallScopeProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> ProjectRootProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> SkillsDirectoryProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> SubagentsDirectoryProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<bool> InstallSkillsProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> InstallSubagentsProperty { get; } = new ValueOnlyProperty<bool>(true);

    // One MCP server covers live editing of running editors and headless file editing.
    public IProperty<bool> InstallMcpProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<string> McpConfigFileNameProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> McpServersPropertyNameProperty { get; } = new ValueOnlyProperty<string>("");

    // Keep legacy settings until migration succeeds, then omit this property from new saves.
    public IProperty<string> LiveMcpTokenProperty { get; } = new ValueOnlyProperty<string>("");

    // Empty means "use the detected stdio launcher"; a non-empty value is the user's override, which must
    // survive reopening the settings page.
    public IProperty<string> StdioCommandProperty { get; } = new ValueOnlyProperty<string>("");

    public IProperty<string> StdioArgumentsProperty { get; } = new ValueOnlyProperty<string>("");

    // Experimental: move the editor to each live MCP edit and rendered frame. Off by default because it
    // takes over the selection, playhead and scroll position.
    public IProperty<bool> FollowLiveMcpEditsProperty { get; } = new ValueOnlyProperty<bool>(false);

    // Empty means "use the host-computed default" (first catalog agent, project
    // scope, or the selected agent's own conventions).
    public string AgentId
    {
        get => AgentIdProperty.Value;
        set => AgentIdProperty.Value = value;
    }

    public string InstallScope
    {
        get => InstallScopeProperty.Value;
        set => InstallScopeProperty.Value = value;
    }

    public string ProjectRoot
    {
        get => ProjectRootProperty.Value;
        set => ProjectRootProperty.Value = value;
    }

    public string SkillsDirectory
    {
        get => SkillsDirectoryProperty.Value;
        set => SkillsDirectoryProperty.Value = value;
    }

    public string SubagentsDirectory
    {
        get => SubagentsDirectoryProperty.Value;
        set => SubagentsDirectoryProperty.Value = value;
    }

    public bool InstallSkills
    {
        get => InstallSkillsProperty.Value;
        set => InstallSkillsProperty.Value = value;
    }

    public bool InstallSubagents
    {
        get => InstallSubagentsProperty.Value;
        set => InstallSubagentsProperty.Value = value;
    }

    public bool InstallMcp
    {
        get => InstallMcpProperty.Value;
        set => InstallMcpProperty.Value = value;
    }

    public string McpConfigFileName
    {
        get => McpConfigFileNameProperty.Value;
        set => McpConfigFileNameProperty.Value = value;
    }

    public string McpServersPropertyName
    {
        get => McpServersPropertyNameProperty.Value;
        set => McpServersPropertyNameProperty.Value = value;
    }

    public string LiveMcpToken
    {
        get => LiveMcpTokenProperty.Value;
        set => LiveMcpTokenProperty.Value = value;
    }

    public string StdioCommand
    {
        get => StdioCommandProperty.Value;
        set => StdioCommandProperty.Value = value;
    }

    public string StdioArguments
    {
        get => StdioArgumentsProperty.Value;
        set => StdioArgumentsProperty.Value = value;
    }

    public bool FollowLiveMcpEdits
    {
        get => FollowLiveMcpEditsProperty.Value;
        set => FollowLiveMcpEditsProperty.Value = value;
    }

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
            InstallMcp = false;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        // A settings save (including startup failure recovery) must not erase the only copy
        // of a legacy credential before the endpoint has durably migrated it.
        if (string.IsNullOrWhiteSpace(LiveMcpToken))
            context.SetValue(nameof(LiveMcpToken), Unit.Default);
    }
}
