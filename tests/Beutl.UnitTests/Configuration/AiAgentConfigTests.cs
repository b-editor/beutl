using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class AiAgentConfigTests
{
    [Test]
    public void Defaults_install_the_mcp_server()
    {
        var config = new AiAgentConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.AgentId.Value, Is.Empty);
            Assert.That(config.InstallScope.Value, Is.Empty);
            Assert.That(config.ProjectRoot.Value, Is.Empty);
            Assert.That(config.SkillsDirectory.Value, Is.Empty);
            Assert.That(config.SubagentsDirectory.Value, Is.Empty);
            Assert.That(config.InstallSkills.Value, Is.True);
            Assert.That(config.InstallSubagents.Value, Is.True);
            Assert.That(config.InstallMcp.Value, Is.True);
            Assert.That(config.McpConfigFileName.Value, Is.Empty);
            Assert.That(config.McpServersPropertyName.Value, Is.Empty);
            Assert.That(config.LiveMcpToken.Value, Is.Empty);
            Assert.That(config.FollowLiveMcpEdits.Value, Is.False);
            Assert.That(config.DefaultRuntimeProviderId.Value, Is.Empty);
            Assert.That(config.DefaultPermissionMode.Value, Is.EqualTo("Auto"));
            Assert.That(config.DefaultModel.Value, Is.Empty);
            Assert.That(config.DefaultReasoningEffort.Value, Is.Empty);
            Assert.That(config.SteerOnSend.Value, Is.False);
            Assert.That(config.ComposerSendKey.Value, Is.EqualTo(AgentComposerSendKey.Enter));
            Assert.That(config.EnableNotifications.Value, Is.True);
            Assert.That(config.EnableNotificationSound.Value, Is.True);
            Assert.That(config.ClaudeCodeExecutablePath.Value, Is.Empty);
            Assert.That(config.CodexExecutablePath.Value, Is.Empty);
            Assert.That(config.CustomRuntimeProvidersJson.Value, Is.Empty);
        });
    }

    [Test]
    public void Serialization_roundtrips_settings_without_persisting_the_legacy_token()
    {
        var source = new AiAgentConfig
        {
            AgentId = { Value = "codex" },
            InstallScope = { Value = "Global" },
            ProjectRoot = { Value = "/repo" },
            SkillsDirectory = { Value = ".claude/skills" },
            SubagentsDirectory = { Value = ".claude/agents" },
            InstallSkills = { Value = false },
            InstallSubagents = { Value = false },
            InstallMcp = { Value = false },
            McpConfigFileName = { Value = "mcp.json" },
            McpServersPropertyName = { Value = "mcpServers" },
            FollowLiveMcpEdits = { Value = true },
            DefaultRuntimeProviderId = { Value = "custom-acp" },
            DefaultPermissionMode = { Value = "Supervised" },
            DefaultModel = { Value = "example-model" },
            DefaultReasoningEffort = { Value = "high" },
            SteerOnSend = { Value = true },
            ComposerSendKey = { Value = AgentComposerSendKey.CtrlEnter },
            EnableNotifications = { Value = false },
            EnableNotificationSound = { Value = false },
            ClaudeCodeExecutablePath = { Value = "/tools/claude" },
            CodexExecutablePath = { Value = "/tools/codex" },
            CustomRuntimeProvidersJson = { Value = """[{"id":"custom-acp","displayName":"Example","command":"acp","arguments":[]}]""" },
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.AgentId.Value, Is.EqualTo(source.AgentId.Value));
            Assert.That(restored.InstallScope.Value, Is.EqualTo(source.InstallScope.Value));
            Assert.That(restored.ProjectRoot.Value, Is.EqualTo(source.ProjectRoot.Value));
            Assert.That(restored.SkillsDirectory.Value, Is.EqualTo(source.SkillsDirectory.Value));
            Assert.That(restored.SubagentsDirectory.Value, Is.EqualTo(source.SubagentsDirectory.Value));
            Assert.That(restored.InstallSkills.Value, Is.EqualTo(source.InstallSkills.Value));
            Assert.That(restored.InstallSubagents.Value, Is.EqualTo(source.InstallSubagents.Value));
            Assert.That(restored.InstallMcp.Value, Is.EqualTo(source.InstallMcp.Value));
            Assert.That(restored.McpConfigFileName.Value, Is.EqualTo(source.McpConfigFileName.Value));
            Assert.That(restored.McpServersPropertyName.Value, Is.EqualTo(source.McpServersPropertyName.Value));
            Assert.That(restored.FollowLiveMcpEdits.Value, Is.True);
            Assert.That(restored.DefaultRuntimeProviderId.Value, Is.EqualTo(source.DefaultRuntimeProviderId.Value));
            Assert.That(restored.DefaultPermissionMode.Value, Is.EqualTo(source.DefaultPermissionMode.Value));
            Assert.That(restored.DefaultModel.Value, Is.EqualTo(source.DefaultModel.Value));
            Assert.That(restored.DefaultReasoningEffort.Value, Is.EqualTo("high"));
            Assert.That(restored.SteerOnSend.Value, Is.True);
            Assert.That(restored.ComposerSendKey.Value, Is.EqualTo(AgentComposerSendKey.CtrlEnter));
            Assert.That(restored.EnableNotifications.Value, Is.False);
            Assert.That(restored.EnableNotificationSound.Value, Is.False);
            Assert.That(restored.ClaudeCodeExecutablePath.Value, Is.EqualTo(source.ClaudeCodeExecutablePath.Value));
            Assert.That(restored.CodexExecutablePath.Value, Is.EqualTo(source.CodexExecutablePath.Value));
            Assert.That(restored.CustomRuntimeProvidersJson.Value, Is.EqualTo(source.CustomRuntimeProvidersJson.Value));
            Assert.That(json.ContainsKey(nameof(AiAgentConfig.LiveMcpToken)), Is.False);
            Assert.That(restored.LiveMcpToken.Value, Is.Empty);
        });
    }

    [Test]
    public void Profiles_without_picker_settings_preserve_the_configured_model()
    {
        var config = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(config, new JsonObject { [nameof(AiAgentConfig.DefaultModel)] = "example-model" });
        Assert.That(config.ComposerSendKey.Value, Is.EqualTo(AgentComposerSendKey.Enter));

        Assert.That(config.DefaultModel.Value, Is.EqualTo("example-model"));
        Assert.That(config.DefaultReasoningEffort.Value, Is.Empty);
        Assert.That(config.EnableNotifications.Value, Is.True);
        Assert.That(config.EnableNotificationSound.Value, Is.True);
    }

    [Test]
    public void Notification_changes_raise_RuntimeSettingsChanged()
    {
        var config = new AiAgentConfig();
        var changes = new List<string?>();
        config.RuntimeSettingsChanged += (_, args) => changes.Add(args.PropertyName);

        config.EnableNotifications.Value = false;
        config.EnableNotificationSound.Value = false;

        Assert.That(changes, Is.EqualTo(new[] { nameof(config.EnableNotifications), nameof(config.EnableNotificationSound) }));
    }

    [Test]
    public void Property_change_raises_ConfigurationChanged()
    {
        var config = new AiAgentConfig();
        int raised = 0;
        config.ConfigurationChanged += (_, _) => raised++;

        config.AgentId.Value = "claude-code";

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void Legacy_profiles_that_turned_both_mcp_options_off_stay_opted_out()
    {
        var optedOut = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(optedOut,
            new JsonObject { ["InstallStdioMcp"] = false, ["InstallLiveMcp"] = false });
        var liveOnly = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(liveOnly,
            new JsonObject { ["InstallStdioMcp"] = false, ["InstallLiveMcp"] = true });
        var explicitOn = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(explicitOn,
            new JsonObject { ["InstallStdioMcp"] = false, ["InstallLiveMcp"] = false, [nameof(AiAgentConfig.InstallMcp)] = true });

        Assert.Multiple(() =>
        {
            Assert.That(optedOut.InstallMcp.Value, Is.False);
            Assert.That(liveOnly.InstallMcp.Value, Is.True);
            Assert.That(explicitOn.InstallMcp.Value, Is.True);
        });
    }

    [Test]
    public void Profiles_saved_with_a_workspace_root_load_and_drop_it_on_the_next_save()
    {
        var config = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(config,
            new JsonObject { ["WorkspaceRoot"] = "/videos", [nameof(AiAgentConfig.ProjectRoot)] = "/repo" });

        Assert.Multiple(() =>
        {
            Assert.That(config.ProjectRoot.Value, Is.EqualTo("/repo"));
            Assert.That(CoreSerializer.SerializeToJsonObject(config).ContainsKey("WorkspaceRoot"), Is.False);
        });
    }

    [Test]
    public void Legacy_token_is_preserved_until_migration_then_omitted_from_new_saves()
    {
        var config = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(config,
            new JsonObject { [nameof(AiAgentConfig.LiveMcpToken)] = "legacy-migration-token" });
        Assert.That(config.LiveMcpToken.Value, Is.EqualTo("legacy-migration-token"));
        Assert.That((string?)CoreSerializer.SerializeToJsonObject(config)[nameof(AiAgentConfig.LiveMcpToken)],
            Is.EqualTo("legacy-migration-token"));

        config.LiveMcpToken.Value = "";
        Assert.That(CoreSerializer.SerializeToJsonObject(config).ContainsKey(nameof(AiAgentConfig.LiveMcpToken)), Is.False);
    }
}
