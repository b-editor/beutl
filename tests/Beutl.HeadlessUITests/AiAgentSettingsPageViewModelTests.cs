using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Installation;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using Tomlyn;
using Tomlyn.Model;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiAgentSettingsPageViewModelTests
{
    private static AiAgentSettingsPageViewModel CreateViewModel(AiAgentConfig config)
    {
        var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);
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
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Not.Contain("beutl-agent"));
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
    public void Codex_uses_its_config_file_and_converts_subagents()
    {
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(new AiAgentConfig());

        viewModel.SelectedAgent.Value = Choice(viewModel, "codex");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.EndWith("config.toml"));
            // The endpoint has not started yet.
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
    [NonParallelizable]
    [TestCase(AgentInstallScope.Global)]
    [TestCase(AgentInstallScope.Project)]
    public async Task Switching_to_codex_ignores_other_agents_mcp_overrides(AgentInstallScope scope)
    {
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "codex-overrides-" + Guid.NewGuid().ToString("N"));
        string codexHome = Path.Combine(root, "global-codex-home");
        string? previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);
        try
        {
            var config = new AiAgentConfig
            {
                AgentId = AiAgentSettingsPageViewModel.CustomAgentId,
                InstallScope = scope.ToString(),
                ProjectRoot = root,
                WorkspaceRoot = root,
                McpConfigFileName = "other-mcp.json",
                McpServersPropertyName = "mcpServers",
                InstallSkills = false,
                InstallSubagents = false,
                InstallLiveMcp = false,
                InstallStdioMcp = true,
                StdioCommand = "beutl-mcp",
            };
            using AiAgentSettingsPageViewModel viewModel = CreateViewModel(config);
            viewModel.SelectedAgent.Value = Choice(viewModel, "codex");
            string expected = scope == AgentInstallScope.Global ? Path.Combine(codexHome, "config.toml")
                : Path.Combine(root, ".codex", "config.toml");
            // Check the destination before allowing the global install to write anything.
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Is.EqualTo(expected));

            await viewModel.InstallAsync();

            Assert.That(File.Exists(expected), Is.True, viewModel.Status.Value);
            TomlTable saved = TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(expected))!;
            Assert.That(saved.Keys, Is.EqualTo(new[] { "mcp_servers" }));
            Assert.That(((TomlTable)saved["mcp_servers"]).ContainsKey("beutl-agent"), Is.True);

            viewModel.SelectedAgent.Value = Choice(viewModel, AiAgentSettingsPageViewModel.CustomAgentId);
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Is.EqualTo(Path.Combine(root, "other-mcp.json")));
            Assert.That(config.McpConfigFileName, Is.EqualTo("other-mcp.json"));
            Assert.That(config.McpServersPropertyName, Is.EqualTo("mcpServers"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
        }
    }

    [AvaloniaTest]
    [NonParallelizable]
    public async Task Relative_codex_home_blocks_global_mcp_installation_but_not_project_scope()
    {
        string? previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        string relativeHome = "codex-relative-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("CODEX_HOME", relativeHome);
        try
        {
            var config = new AiAgentConfig
            {
                AgentId = "codex",
                InstallScope = nameof(AgentInstallScope.Global),
                ProjectRoot = Path.Combine(BeutlHomeIsolation.CurrentHome!, relativeHome),
                InstallSkills = false,
                InstallSubagents = false,
                InstallLiveMcp = false,
                InstallStdioMcp = true,
                StdioCommand = "beutl-mcp",
            };
            using AiAgentSettingsPageViewModel viewModel = CreateViewModel(config);
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.CanInstallMcp.Value, Is.False);
                Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Contain("CODEX_HOME"));
            });

            await viewModel.InstallAsync();

            Assert.That(viewModel.Status.Value, Does.Contain("CODEX_HOME"));
            Assert.That(viewModel.McpUnavailableMessage.Value, Is.EqualTo(viewModel.Status.Value));
            Assert.That(viewModel.InstalledFiles, Is.Empty);
            Assert.That(Directory.Exists(relativeHome), Is.False);

            var page = new AiAgentSettingsPage { DataContext = viewModel };
            var window = new Window { Content = page, Width = 800, Height = 1000 };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render();
                var warning = page.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAInfoBar>()
                    .Single(bar => bar.Message == viewModel.McpUnavailableMessage.Value);
                warning.BringIntoView();
                HeadlessTestHelpers.Render();
                Assert.That(warning.IsOpen, Is.True);
                if (Environment.GetEnvironmentVariable("BEUTL_CODEX_MCP_CAPTURE") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var image = window.CaptureRenderedFrame();
                    image!.Save(Path.Combine(directory, "codex-relative-home.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
            }

            viewModel.SelectedScope.Value = viewModel.ScopeChoices.Single(s => s.Scope == AgentInstallScope.Project);
            Assert.That(viewModel.CanInstallMcp.Value, Is.True);
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Is.EqualTo(Path.Combine(config.ProjectRoot, ".codex", "config.toml")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
        }
    }

    [AvaloniaTest]
    [NonParallelizable]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Relative_codex_home_does_not_block_selected_assets(bool installSkills, bool installSubagents)
    {
        string temporaryDirectory = ".beutl-codex-review-test-" + Guid.NewGuid().ToString("N");
        string assetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), temporaryDirectory);
        string? previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Environment.SetEnvironmentVariable("CODEX_HOME", "relative-codex-home");
        try
        {
            var config = new AiAgentConfig
            {
                AgentId = "codex",
                InstallScope = nameof(AgentInstallScope.Global),
                SkillsDirectory = Path.Combine(temporaryDirectory, "skills"),
                SubagentsDirectory = Path.Combine(temporaryDirectory, "agents"),
                InstallSkills = installSkills,
                InstallSubagents = installSubagents,
                InstallLiveMcp = true,
                InstallStdioMcp = false,
            };
            using AiAgentSettingsPageViewModel viewModel = CreateViewModel(config);
            Assert.That(viewModel.CanInstallMcp.Value, Is.False);

            await viewModel.InstallAsync();

            Assert.That(viewModel.InstalledFiles, Is.Not.Empty, viewModel.Status.Value);
            Assert.That(Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories), Is.Not.Empty);
            Assert.That(viewModel.InstalledFiles.Any(file => file.EndsWith("config.toml", StringComparison.Ordinal)), Is.False);
            Assert.That(viewModel.Status.Value, Does.StartWith(string.Format(SettingsStrings.AiAgents_InstallCompleted, viewModel.InstalledFiles.Count)));
            Assert.That(viewModel.Status.Value, Does.Contain(viewModel.McpUnavailableMessage.Value));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
            if (Directory.Exists(assetRoot))
                Directory.Delete(assetRoot, recursive: true);
        }
    }

    [AvaloniaTest]
    [TestCase(AgentInstallScope.Global)]
    [TestCase(AgentInstallScope.Project)]
    public async Task Codex_live_mcp_is_available_in_both_scopes(AgentInstallScope scope)
    {
        var config = new AiAgentConfig { AgentId = "codex", InstallScope = scope.ToString(), ProjectRoot = "/repo" };
        await using var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);
        await endpoint.StartAsync();
        using var viewModel = new AiAgentSettingsPageViewModel(endpoint, config);

        string root = scope == AgentInstallScope.Project
            ? Path.Combine("/repo", ".codex")
            : Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codexHome
                ? Path.GetFullPath(codexHome)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanInstallLiveMcp.Value, Is.True);
            Assert.That(viewModel.InstallLiveMcp.Value, Is.True);
            Assert.That(viewModel.InstallStdioMcp.Value, Is.False);
            Assert.That(viewModel.ResolvedMcpConfigPath.Value, Is.EqualTo(Path.Combine(root, "config.toml")));
        });
    }

    [AvaloniaTest]
    public async Task Cli_preview_tracks_selected_transports_without_a_stdio_fallback()
    {
        var config = new AiAgentConfig();
        await using var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);
        await endpoint.StartAsync();
        using var viewModel = new AiAgentSettingsPageViewModel(endpoint, config);

        Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Contain("beutl-live").And.Not.Contain("beutl-agent"));
        viewModel.InstallLiveMcp.Value = false;
        Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Not.Contain("mcp add"));
        viewModel.InstallStdioMcp.Value = true;
        Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Contain("beutl-agent").And.Not.Contain("beutl-live"));
        viewModel.InstallLiveMcp.Value = true;
        Assert.That(viewModel.ResolvedMcpConfigPath.Value, Does.Contain("beutl-agent").And.Contain("beutl-live"));
    }

    [AvaloniaTest]
    [TestCase(480, false)]
    [TestCase(800, true)]
    public async Task Codex_settings_install_live_mcp_and_display_the_actual_config_path(int width, bool light)
    {
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "codex-install-" + Guid.NewGuid().ToString("N"));
        var config = new AiAgentConfig
        {
            AgentId = "codex",
            InstallScope = nameof(AgentInstallScope.Project),
            ProjectRoot = root,
            McpConfigFileName = "legacy-mcp.json",
            McpServersPropertyName = "mcpServers",
            InstallSkills = false,
            InstallSubagents = false,
            LiveMcpToken = "headless-test-token",
        };
        await using var endpoint = new AgentHostEndpoint(new ProjectService(), new EditorService(new ExtensionProvider()), config);
        await endpoint.StartAsync();
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        using var viewModel = new AiAgentSettingsPageViewModel(endpoint, config);
        var page = new AiAgentSettingsPage { DataContext = viewModel };
        var window = new Window
        {
            Content = page,
            Width = width,
            Height = 1000,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            OptionsDisplayItem[] rows = page.GetVisualDescendants().OfType<OptionsDisplayItem>().ToArray();
            OptionsDisplayItem destinations = rows.Single(row => Equals(row.Header, SettingsStrings.AiAgents_Destinations));
            destinations.ContentTransition = null;
            destinations.IsExpanded = true;
            OptionsDisplayItem advanced = rows.Single(row => Equals(row.Header, SettingsStrings.AiAgents_Advanced));
            advanced.ContentTransition = null;
            advanced.IsExpanded = true;
            HeadlessTestHelpers.Render();

            OptionsDisplayItem[] overrides = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                .Where(row => row.ActionButton is TextBox
                              && (Equals(row.Header, SettingsStrings.AiAgents_McpConfigFileName)
                                  || Equals(row.Header, SettingsStrings.AiAgents_McpServersPropertyName)))
                .ToArray();
            Assert.That(overrides, Has.Length.EqualTo(2));
            Assert.That(overrides.All(row => !row.IsVisible), Is.True);

            OptionsDisplayItem liveRow = rows.Single(row => Equals(row.Header, SettingsStrings.AiAgents_LiveMcp));
            OptionsDisplayItem stdioRow = rows.Single(row => Equals(row.Header, SettingsStrings.AiAgents_StdioMcp));
            var liveToggle = (ToggleSwitch)liveRow.ActionButton!;
            var stdioToggle = (ToggleSwitch)stdioRow.ActionButton!;
            Assert.Multiple(() =>
            {
                Assert.That(liveRow.IsEnabled, Is.True);
                Assert.That(liveToggle.IsChecked, Is.True);
                Assert.That(stdioToggle.IsChecked, Is.False);
            });

            await viewModel.InstallAsync();

            string path = viewModel.ResolvedMcpConfigPath.Value;
            Assert.That(path, Is.EqualTo(Path.Combine(root, ".codex", "config.toml")));
            Assert.That(File.Exists(path), Is.True, viewModel.Status.Value);
            var servers = (TomlTable)TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(path))!["mcp_servers"];
            var live = (TomlTable)servers["beutl-live"];
            Assert.Multiple(() =>
            {
                Assert.That(servers.ContainsKey("beutl-agent"), Is.False);
                Assert.That(live["url"], Is.EqualTo(endpoint.EndpointUri!.ToString()));
                Assert.That(((TomlTable)live["http_headers"])["Authorization"], Is.EqualTo("Bearer headless-test-token"));
                Assert.That(viewModel.InstalledFiles, Has.Count.EqualTo(1));
                Assert.That(page.GetVisualDescendants().OfType<SelectableTextBlock>().Any(block => block.Text == path), Is.True);
            });

            stdioRow.BringIntoView();
            HeadlessTestHelpers.Render();
            if (Environment.GetEnvironmentVariable("BEUTL_CODEX_MCP_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"codex-live-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
                File.Copy(path, Path.Combine(directory, "config.toml"), overwrite: true);
                advanced.BringIntoView();
                HeadlessTestHelpers.Render();
                using var advancedImage = window.CaptureRenderedFrame();
                advancedImage!.Save(Path.Combine(directory, $"codex-advanced-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
            }

            viewModel.SelectedAgent.Value = Choice(viewModel, AiAgentSettingsPageViewModel.CustomAgentId);
            HeadlessTestHelpers.Render();
            Assert.That(overrides.All(row => row.IsVisible), Is.True);
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
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
    public async Task Install_writes_a_manifest_and_prunes_stale_unmodified_files()
    {
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, "agent-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new AiAgentConfig();
        using AiAgentSettingsPageViewModel viewModel = CreateViewModel(config);
        viewModel.SelectedAgent.Value = Choice(viewModel, AiAgentSettingsPageViewModel.CustomAgentId);
        viewModel.ProjectRoot.Value = root;
        viewModel.WorkspaceRoot.Value = root;
        viewModel.InstallStdioMcp.Value = false;
        viewModel.InstallLiveMcp.Value = false;

        await viewModel.InstallAsync();

        string manifestPath = AgentToolkitInstallManifestStore.GetDefaultPath();
        AgentToolkitInstallManifest? manifest = AgentToolkitInstallManifestStore.Load(manifestPath);
        Assert.That(manifest, Is.Not.Null, viewModel.Status.Value);
        Assert.That(manifest!.Files, Is.Not.Empty);

        // Simulate a skill the previous app version installed but the new
        // bundle no longer ships: unmodified → pruned on reinstall.
        string stalePath = Path.Combine(root, "skills", "beutl-agent-effectItem", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
        File.WriteAllText(stalePath, "effectItem");
        string editedPath = Path.Combine(root, "skills", "beutl-agent-edited", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(editedPath)!);
        File.WriteAllText(editedPath, "user edits");
        AgentToolkitInstallManifestStore.Save(manifestPath, manifest with
        {
            Files =
            [
                .. manifest.Files,
                new InstalledFileRecord(stalePath, AgentToolkitInstallManifestStore.ComputeContentHash("effectItem")),
                new InstalledFileRecord(editedPath, AgentToolkitInstallManifestStore.ComputeContentHash("shipped")),
            ],
        });

        await viewModel.InstallAsync();

        AgentToolkitInstallManifest? updated = AgentToolkitInstallManifestStore.Load(manifestPath);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(stalePath), Is.False);
            Assert.That(File.Exists(editedPath), Is.True);
            Assert.That(viewModel.InstalledFiles, Does.Contain("removed: " + stalePath));
            Assert.That(updated!.Files.Select(f => f.Path), Does.Not.Contain(stalePath));
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

    [AvaloniaTest]
    public void Customized_stdio_command_survives_reopening()
    {
        var config = new AiAgentConfig();
        using (AiAgentSettingsPageViewModel viewModel = CreateViewModel(config))
        {
            viewModel.McpCommand.Value = "/custom/beutl-agent";
            viewModel.McpArguments.Value = "mcp\n--flag";
        }

        Assert.Multiple(() =>
        {
            Assert.That(config.StdioCommand, Is.EqualTo("/custom/beutl-agent"));
            Assert.That(config.StdioArguments, Is.EqualTo("mcp\n--flag"));
        });

        // A fresh view model (reopen, or the reinstall update-prompt path) must keep the override
        // instead of reverting to the detected launcher.
        using AiAgentSettingsPageViewModel restored = CreateViewModel(config);
        Assert.Multiple(() =>
        {
            Assert.That(restored.McpCommand.Value, Is.EqualTo("/custom/beutl-agent"));
            Assert.That(restored.McpArguments.Value, Is.EqualTo("mcp\n--flag"));
        });
    }
}
