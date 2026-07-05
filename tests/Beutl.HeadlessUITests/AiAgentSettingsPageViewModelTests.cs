using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Installation;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiAgentSettingsPageViewModelTests
{
    private static AiAgentSettingsPageViewModel CreateViewModel(AiAgentConfig config)
    {
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()));
        return new AiAgentSettingsPageViewModel(endpoint, config);
    }

    private static AgentChoiceItem Choice(AiAgentSettingsPageViewModel viewModel, string id)
    {
        return viewModel.AgentChoices.Single(a => a.Id == id);
    }

    [AvaloniaTest]
    public void Empty_config_defaults_to_first_catalog_agent_and_global_scope()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedAgent.Value.Id, Is.EqualTo("claude-code"));
            Assert.That(viewModel.SelectedScope.Value.Scope, Is.EqualTo(AgentInstallScope.Global));
            Assert.That(viewModel.IsProjectFolderVisible.Value, Is.False);
            Assert.That(viewModel.CanInstallSubagents.Value, Is.True);
            // ~/.claude.json is app-managed, so global MCP goes through `claude mcp add`.
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(
                viewModel.ResolvedSkillsPath.Value,
                Is.EqualTo(Path.Combine(home, ".claude", "skills")));
            Assert.That(
                viewModel.ResolvedMcpConfigPath.Value,
                Does.StartWith("$ claude mcp add --scope user"));
            Assert.That(viewModel.WorkspaceRoot.Value, Is.Not.Empty);
            Assert.That(viewModel.McpCommand.Value, Is.Not.Empty);
            // Live MCP is the primary integration; stdio is opt-in.
            Assert.That(viewModel.InstallLiveMcp.Value, Is.True);
            Assert.That(viewModel.InstallStdioMcp.Value, Is.False);
            Assert.That(viewModel.CanInstallStdioMcp.Value, Is.True);
            Assert.That(viewModel.IsStdioCommandMissing.Value, Is.False);
        });
    }

    [AvaloniaTest]
    public void Missing_stdio_command_disables_the_stdio_toggle()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.McpCommand.Value = "";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallStdioMcp.Value, Is.False);
            Assert.That(viewModel.IsStdioCommandMissing.Value, Is.True);
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
        });
    }

    [AvaloniaTest]
    public void Claude_code_project_scope_enables_mcp_at_the_repo_root()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedScope.Value = viewModel.ScopeChoices.Single(
            s => s.Scope == AgentInstallScope.Project);
        viewModel.ProjectRoot.Value = "/repo";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(
                viewModel.ResolvedMcpConfigPath.Value,
                Is.EqualTo(Path.Combine("/repo", ".mcp.json")));
        });
    }

    [AvaloniaTest]
    public void Codex_registers_mcp_through_its_cli_and_converts_subagents()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedAgent.Value = Choice(viewModel, "codex");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.StartWith("$ codex mcp add"));
            // Live MCP needs a remote entry, which `codex mcp add` does not cover.
            Assert.That(viewModel.CanInstallLiveMcp.Value, Is.False);
            Assert.That(viewModel.CanInstallSubagents.Value, Is.True);
            Assert.That(
                viewModel.ResolvedSubagentsPath.Value,
                Does.EndWith(Path.Combine(".codex", "agents")));
            Assert.That(
                viewModel.ResolvedSkillsPath.Value,
                Does.EndWith(Path.Combine(".agents", "skills")));
        });
    }

    [AvaloniaTest]
    public void Agent_without_mcp_support_disables_mcp()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedAgent.Value = Choice(viewModel, "goose");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallMcp.Value, Is.False);
            Assert.That(viewModel.CanInstallSubagents.Value, Is.False);
            Assert.That(
                viewModel.ResolvedMcpConfigPath.Value,
                Is.EqualTo(Beutl.Language.SettingsStrings.AiAgents_NotSupported));
        });
    }

    [AvaloniaTest]
    public void Scope_change_switches_between_home_and_project_paths()
    {
        var config = new AiAgentConfig();
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(config);
        viewModel.SelectedAgent.Value = Choice(viewModel, "codex");
        viewModel.ProjectRoot.Value = "/repo";

        viewModel.SelectedScope.Value = viewModel.ScopeChoices.Single(
            s => s.Scope == AgentInstallScope.Project);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsProjectFolderVisible.Value, Is.True);
            Assert.That(
                viewModel.ResolvedSkillsPath.Value,
                Is.EqualTo(Path.Combine("/repo", ".agents", "skills")));
        });
    }

    [AvaloniaTest]
    public void Custom_agent_uses_manual_paths_and_hides_scope()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedAgent.Value = Choice(viewModel, AiAgentSettingsPageViewModel.CustomAgentId);
        viewModel.ProjectRoot.Value = "/anywhere";
        viewModel.SkillsDirectory.Value = "my-skills";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCustomAgent.Value, Is.True);
            Assert.That(viewModel.IsScopeSelectable.Value, Is.False);
            Assert.That(viewModel.IsProjectFolderVisible.Value, Is.True);
            Assert.That(viewModel.CanInstallSubagents.Value, Is.True);
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(
                viewModel.ResolvedSkillsPath.Value,
                Is.EqualTo(Path.Combine("/anywhere", "my-skills")));
        });
    }

    [AvaloniaTest]
    public void Endpoint_not_running_reports_live_mcp_unavailable()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsLiveMcpAvailable.Value, Is.False);
            Assert.That(viewModel.CanInstallLiveMcp.Value, Is.False);
            Assert.That(viewModel.LiveMcpUrl.Value, Is.Empty);
        });
    }

    [AvaloniaTest]
    public void Edits_write_through_to_config_and_restore_in_a_new_view_model()
    {
        var config = new AiAgentConfig();
        using (AiAgentSettingsPageViewModel viewModel = CreateViewModel(config))
        {
            viewModel.SelectedAgent.Value = Choice(viewModel, "cursor");
            viewModel.SelectedScope.Value = viewModel.ScopeChoices.Single(
                s => s.Scope == AgentInstallScope.Project);
            viewModel.ProjectRoot.Value = "/repo";
            viewModel.WorkspaceRoot.Value = "/videos";
            viewModel.InstallStdioMcp.Value = false;
            viewModel.McpConfigFileName.Value = "custom-mcp.json";
        }

        Assert.Multiple(() =>
        {
            Assert.That(config.AgentId, Is.EqualTo("cursor"));
            Assert.That(config.InstallScope, Is.EqualTo(nameof(AgentInstallScope.Project)));
            Assert.That(config.ProjectRoot, Is.EqualTo("/repo"));
            Assert.That(config.WorkspaceRoot, Is.EqualTo("/videos"));
            Assert.That(config.InstallStdioMcp, Is.False);
            Assert.That(config.McpConfigFileName, Is.EqualTo("custom-mcp.json"));
        });

        using AiAgentSettingsPageViewModel restored = CreateViewModel(config);
        Assert.Multiple(() =>
        {
            Assert.That(restored.SelectedAgent.Value.Id, Is.EqualTo("cursor"));
            Assert.That(restored.SelectedScope.Value.Scope, Is.EqualTo(AgentInstallScope.Project));
            Assert.That(restored.ProjectRoot.Value, Is.EqualTo("/repo"));
            Assert.That(restored.InstallStdioMcp.Value, Is.False);
            Assert.That(
                restored.ResolvedMcpConfigPath.Value,
                Is.EqualTo(Path.Combine("/repo", "custom-mcp.json")));
        });
    }
}
