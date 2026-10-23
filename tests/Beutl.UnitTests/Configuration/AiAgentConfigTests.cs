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
            Assert.That(json.ContainsKey(nameof(AiAgentConfig.LiveMcpToken)), Is.False);
            Assert.That(restored.LiveMcpToken.Value, Is.Empty);
        });
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
