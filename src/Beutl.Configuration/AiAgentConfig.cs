using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class AiAgentConfig : ConfigurationBase
{
    public static readonly CoreProperty<string> AgentRootProperty;
    public static readonly CoreProperty<string> WorkspaceRootProperty;
    public static readonly CoreProperty<string> InstallLayoutProperty;
    public static readonly CoreProperty<string> SkillsDirectoryProperty;
    public static readonly CoreProperty<string> SubagentsDirectoryProperty;
    public static readonly CoreProperty<bool> InstallSkillsProperty;
    public static readonly CoreProperty<bool> InstallSubagentsProperty;
    public static readonly CoreProperty<bool> InstallStdioMcpProperty;
    public static readonly CoreProperty<bool> InstallLiveMcpProperty;
    public static readonly CoreProperty<string> McpConfigFileNameProperty;
    public static readonly CoreProperty<string> McpServersPropertyNameProperty;

    static AiAgentConfig()
    {
        AgentRootProperty = ConfigureProperty<string, AiAgentConfig>(nameof(AgentRoot))
            .DefaultValue("")
            .Register();

        WorkspaceRootProperty = ConfigureProperty<string, AiAgentConfig>(nameof(WorkspaceRoot))
            .DefaultValue("")
            .Register();

        InstallLayoutProperty = ConfigureProperty<string, AiAgentConfig>(nameof(InstallLayout))
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

        InstallStdioMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallStdioMcp))
            .DefaultValue(true)
            .Register();

        InstallLiveMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallLiveMcp))
            .DefaultValue(false)
            .Register();

        McpConfigFileNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpConfigFileName))
            .DefaultValue(".mcp.json")
            .Register();

        McpServersPropertyNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpServersPropertyName))
            .DefaultValue("servers")
            .Register();
    }

    // Empty means "use the host-computed default" (user profile / documents / layout preset).
    public string AgentRoot
    {
        get => GetValue(AgentRootProperty);
        set => SetValue(AgentRootProperty, value);
    }

    public string WorkspaceRoot
    {
        get => GetValue(WorkspaceRootProperty);
        set => SetValue(WorkspaceRootProperty, value);
    }

    public string InstallLayout
    {
        get => GetValue(InstallLayoutProperty);
        set => SetValue(InstallLayoutProperty, value);
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
