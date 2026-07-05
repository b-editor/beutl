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

    [AvaloniaTest]
    public void Empty_config_falls_back_to_host_defaults()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AgentRoot.Value, Is.Not.Empty);
            Assert.That(viewModel.WorkspaceRoot.Value, Is.Not.Empty);
            Assert.That(viewModel.SelectedLayout.Value.Layout, Is.EqualTo(AgentToolkitInstallLayout.Generic));
            Assert.That(viewModel.SkillsDirectory.Value, Is.EqualTo("skills"));
            Assert.That(viewModel.SubagentsDirectory.Value, Is.EqualTo("agents"));
            Assert.That(viewModel.McpConfigFileName.Value, Is.EqualTo(".mcp.json"));
            Assert.That(viewModel.McpServersPropertyName.Value, Is.EqualTo("servers"));
            Assert.That(viewModel.McpCommand.Value, Is.Not.Empty);
        });
    }

    [AvaloniaTest]
    public void Endpoint_not_running_reports_live_mcp_unavailable()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsLiveMcpAvailable.Value, Is.False);
            Assert.That(viewModel.LiveMcpUrl.Value, Is.Empty);
        });
    }

    [AvaloniaTest]
    public void Edits_write_through_to_config_and_restore_in_a_new_view_model()
    {
        var config = new AiAgentConfig();
        using (AiAgentSettingsPageViewModel viewModel = CreateViewModel(config))
        {
            viewModel.AgentRoot.Value = "/repo";
            viewModel.WorkspaceRoot.Value = "/videos";
            viewModel.SelectedLayout.Value = viewModel.Layouts.Single(
                i => i.Layout == AgentToolkitInstallLayout.ClaudeCode);
            viewModel.InstallStdioMcp.Value = false;
            viewModel.McpConfigFileName.Value = "mcp.json";
        }

        Assert.Multiple(() =>
        {
            Assert.That(config.AgentRoot, Is.EqualTo("/repo"));
            Assert.That(config.WorkspaceRoot, Is.EqualTo("/videos"));
            Assert.That(config.InstallLayout, Is.EqualTo(nameof(AgentToolkitInstallLayout.ClaudeCode)));
            Assert.That(config.SkillsDirectory, Is.EqualTo(Path.Combine(".claude", "skills")));
            Assert.That(config.InstallStdioMcp, Is.False);
            Assert.That(config.McpConfigFileName, Is.EqualTo("mcp.json"));
        });

        using AiAgentSettingsPageViewModel restored = CreateViewModel(config);
        Assert.Multiple(() =>
        {
            Assert.That(restored.AgentRoot.Value, Is.EqualTo("/repo"));
            Assert.That(restored.WorkspaceRoot.Value, Is.EqualTo("/videos"));
            Assert.That(restored.SelectedLayout.Value.Layout, Is.EqualTo(AgentToolkitInstallLayout.ClaudeCode));
            Assert.That(restored.SkillsDirectory.Value, Is.EqualTo(Path.Combine(".claude", "skills")));
            Assert.That(restored.InstallStdioMcp.Value, Is.False);
            Assert.That(restored.McpConfigFileName.Value, Is.EqualTo("mcp.json"));
        });
    }

    [AvaloniaTest]
    public void Layout_change_updates_preset_directories()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedLayout.Value = viewModel.Layouts.Single(
            i => i.Layout == AgentToolkitInstallLayout.ClaudeCode);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SkillsDirectory.Value, Is.EqualTo(Path.Combine(".claude", "skills")));
            Assert.That(viewModel.SubagentsDirectory.Value, Is.EqualTo(Path.Combine(".claude", "agents")));
        });
    }
}
