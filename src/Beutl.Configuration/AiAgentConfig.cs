using System.ComponentModel;

using System.Reactive;
using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class AiAgentConfig : ConfigurationBase
{
    public static readonly CoreProperty<string> AgentIdProperty;
    public static readonly CoreProperty<string> InstallScopeProperty;
    public static readonly CoreProperty<string> ProjectRootProperty;
    public static readonly CoreProperty<string> WorkspaceRootProperty;
    public static readonly CoreProperty<string> SkillsDirectoryProperty;
    public static readonly CoreProperty<string> SubagentsDirectoryProperty;
    public static readonly CoreProperty<bool> InstallSkillsProperty;
    public static readonly CoreProperty<bool> InstallSubagentsProperty;
    public static readonly CoreProperty<bool> InstallStdioMcpProperty;
    public static readonly CoreProperty<bool> InstallLiveMcpProperty;
    public static readonly CoreProperty<string> McpConfigFileNameProperty;
    public static readonly CoreProperty<string> McpServersPropertyNameProperty;
    public static readonly CoreProperty<string> LiveMcpTokenProperty;
    public static readonly CoreProperty<string> StdioCommandProperty;
    public static readonly CoreProperty<string> StdioArgumentsProperty;
    public static readonly CoreProperty<bool> FollowLiveMcpEditsProperty;

    static AiAgentConfig()
    {
        AgentIdProperty = ConfigureProperty<string, AiAgentConfig>(nameof(AgentId))
            .DefaultValue("")
            .Register();

        InstallScopeProperty = ConfigureProperty<string, AiAgentConfig>(nameof(InstallScope))
            .DefaultValue("")
            .Register();

        ProjectRootProperty = ConfigureProperty<string, AiAgentConfig>(nameof(ProjectRoot))
            .DefaultValue("")
            .Register();

        WorkspaceRootProperty = ConfigureProperty<string, AiAgentConfig>(nameof(WorkspaceRoot))
            .DefaultValue("")
            .Register();

        SkillsDirectoryProperty = ConfigureProperty<string, AiAgentConfig>(nameof(SkillsDirectory))
            .DefaultValue("")
            .Register();

        SubagentsDirectoryProperty = ConfigureProperty<string, AiAgentConfig>(nameof(SubagentsDirectory))
            .DefaultValue("")
            .Register();

        InstallSkillsProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallSkills))
            .DefaultValue(true)
            .Register();

        InstallSubagentsProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallSubagents))
            .DefaultValue(true)
            .Register();

        // Live MCP (edit the running editor) is the primary integration; the
        // headless stdio server is opt-in.
        InstallStdioMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallStdioMcp))
            .DefaultValue(false)
            .Register();

        InstallLiveMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallLiveMcp))
            .DefaultValue(true)
            .Register();

        McpConfigFileNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpConfigFileName))
            .DefaultValue("")
            .Register();

        McpServersPropertyNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpServersPropertyName))
            .DefaultValue("")
            .Register();

        // Keep legacy settings until migration succeeds, then omit this property from new saves.
        LiveMcpTokenProperty = ConfigureProperty<string, AiAgentConfig>(nameof(LiveMcpToken))
            .DefaultValue("")
            .Register();

        // Empty means "use the detected stdio launcher"; a non-empty value is the user's override,
        // which must survive reopening the settings page.
        StdioCommandProperty = ConfigureProperty<string, AiAgentConfig>(nameof(StdioCommand))
            .DefaultValue("")
            .Register();

        StdioArgumentsProperty = ConfigureProperty<string, AiAgentConfig>(nameof(StdioArguments))
            .DefaultValue("")
            .Register();

        // Experimental: move the editor to each live MCP edit and rendered frame. Off by default
        // because it takes over the selection, playhead and scroll position.
        FollowLiveMcpEditsProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(FollowLiveMcpEdits))
            .DefaultValue(false)
            .Register();
    }

    // Empty means "use the host-computed default" (first catalog agent, project
    // scope, documents folder, or the selected agent's own conventions).
    public string AgentId
    {
        get => GetValue(AgentIdProperty);
        set => SetValue(AgentIdProperty, value);
    }

    public string InstallScope
    {
        get => GetValue(InstallScopeProperty);
        set => SetValue(InstallScopeProperty, value);
    }

    public string ProjectRoot
    {
        get => GetValue(ProjectRootProperty);
        set => SetValue(ProjectRootProperty, value);
    }

    public string WorkspaceRoot
    {
        get => GetValue(WorkspaceRootProperty);
        set => SetValue(WorkspaceRootProperty, value);
    }

    public string SkillsDirectory
    {
        get => GetValue(SkillsDirectoryProperty);
        set => SetValue(SkillsDirectoryProperty, value);
    }

    public string SubagentsDirectory
    {
        get => GetValue(SubagentsDirectoryProperty);
        set => SetValue(SubagentsDirectoryProperty, value);
    }

    public bool InstallSkills
    {
        get => GetValue(InstallSkillsProperty);
        set => SetValue(InstallSkillsProperty, value);
    }

    public bool InstallSubagents
    {
        get => GetValue(InstallSubagentsProperty);
        set => SetValue(InstallSubagentsProperty, value);
    }

    public bool InstallStdioMcp
    {
        get => GetValue(InstallStdioMcpProperty);
        set => SetValue(InstallStdioMcpProperty, value);
    }

    public bool InstallLiveMcp
    {
        get => GetValue(InstallLiveMcpProperty);
        set => SetValue(InstallLiveMcpProperty, value);
    }

    public string McpConfigFileName
    {
        get => GetValue(McpConfigFileNameProperty);
        set => SetValue(McpConfigFileNameProperty, value);
    }

    public string McpServersPropertyName
    {
        get => GetValue(McpServersPropertyNameProperty);
        set => SetValue(McpServersPropertyNameProperty, value);
    }

    public string LiveMcpToken
    {
        get => GetValue(LiveMcpTokenProperty);
        set => SetValue(LiveMcpTokenProperty, value);
    }

    public string StdioCommand
    {
        get => GetValue(StdioCommandProperty);
        set => SetValue(StdioCommandProperty, value);
    }

    public string StdioArguments
    {
        get => GetValue(StdioArgumentsProperty);
        set => SetValue(StdioArgumentsProperty, value);
    }

    public bool FollowLiveMcpEdits
    {
        get => GetValue(FollowLiveMcpEditsProperty);
        set => SetValue(FollowLiveMcpEditsProperty, value);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
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
