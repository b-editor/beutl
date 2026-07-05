using System.ComponentModel;

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

        InstallStdioMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallStdioMcp))
            .DefaultValue(true)
            .Register();

        InstallLiveMcpProperty = ConfigureProperty<bool, AiAgentConfig>(nameof(InstallLiveMcp))
            .DefaultValue(false)
            .Register();

        McpConfigFileNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpConfigFileName))
            .DefaultValue("")
            .Register();

        McpServersPropertyNameProperty = ConfigureProperty<string, AiAgentConfig>(nameof(McpServersPropertyName))
            .DefaultValue("")
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
