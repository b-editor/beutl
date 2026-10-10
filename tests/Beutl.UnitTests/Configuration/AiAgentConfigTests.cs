using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class AiAgentConfigTests
{
    [Test]
    public void Defaults_prefer_live_mcp_and_leave_stdio_opt_in()
    {
        var config = new AiAgentConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.AgentId, Is.Empty);
            Assert.That(config.InstallScope, Is.Empty);
            Assert.That(config.ProjectRoot, Is.Empty);
            Assert.That(config.WorkspaceRoot, Is.Empty);
            Assert.That(config.SkillsDirectory, Is.Empty);
            Assert.That(config.SubagentsDirectory, Is.Empty);
            Assert.That(config.InstallSkills, Is.True);
            Assert.That(config.InstallSubagents, Is.True);
            Assert.That(config.InstallStdioMcp, Is.False);
            Assert.That(config.InstallLiveMcp, Is.True);
            Assert.That(config.McpConfigFileName, Is.Empty);
            Assert.That(config.McpServersPropertyName, Is.Empty);
            Assert.That(config.LiveMcpToken, Is.Empty);
            Assert.That(config.FollowLiveMcpEdits, Is.False);
        });
    }

    [Test]
    public void Serialization_roundtrips_settings_without_persisting_the_legacy_token()
    {
        var source = new AiAgentConfig
        {
            AgentId = "codex",
            InstallScope = "Global",
            ProjectRoot = "/repo",
            WorkspaceRoot = "/videos",
            SkillsDirectory = ".claude/skills",
            SubagentsDirectory = ".claude/agents",
            InstallSkills = false,
            InstallSubagents = false,
            InstallStdioMcp = false,
            InstallLiveMcp = true,
            McpConfigFileName = "mcp.json",
            McpServersPropertyName = "mcpServers",
            FollowLiveMcpEdits = true,
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(source);
        var restored = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.AgentId, Is.EqualTo(source.AgentId));
            Assert.That(restored.InstallScope, Is.EqualTo(source.InstallScope));
            Assert.That(restored.ProjectRoot, Is.EqualTo(source.ProjectRoot));
            Assert.That(restored.WorkspaceRoot, Is.EqualTo(source.WorkspaceRoot));
            Assert.That(restored.SkillsDirectory, Is.EqualTo(source.SkillsDirectory));
            Assert.That(restored.SubagentsDirectory, Is.EqualTo(source.SubagentsDirectory));
            Assert.That(restored.InstallSkills, Is.EqualTo(source.InstallSkills));
            Assert.That(restored.InstallSubagents, Is.EqualTo(source.InstallSubagents));
            Assert.That(restored.InstallStdioMcp, Is.EqualTo(source.InstallStdioMcp));
            Assert.That(restored.InstallLiveMcp, Is.EqualTo(source.InstallLiveMcp));
            Assert.That(restored.McpConfigFileName, Is.EqualTo(source.McpConfigFileName));
            Assert.That(restored.McpServersPropertyName, Is.EqualTo(source.McpServersPropertyName));
            Assert.That(restored.FollowLiveMcpEdits, Is.True);
            Assert.That(json.ContainsKey(nameof(AiAgentConfig.LiveMcpToken)), Is.False);
            Assert.That(restored.LiveMcpToken, Is.Empty);
        });
    }

    [Test]
    public void Property_change_raises_ConfigurationChanged()
    {
        var config = new AiAgentConfig();
        int raised = 0;
        config.ConfigurationChanged += (_, _) => raised++;

        config.AgentId = "claude-code";

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void Legacy_token_is_preserved_until_migration_then_omitted_from_new_saves()
    {
        var config = new AiAgentConfig();
        CoreSerializer.PopulateFromJsonObject(config,
            new JsonObject { [nameof(AiAgentConfig.LiveMcpToken)] = "legacy-migration-token" });
        Assert.That(config.LiveMcpToken, Is.EqualTo("legacy-migration-token"));
        Assert.That((string?)CoreSerializer.SerializeToJsonObject(config)[nameof(AiAgentConfig.LiveMcpToken)],
            Is.EqualTo("legacy-migration-token"));

        config.LiveMcpToken = "";
        Assert.That(CoreSerializer.SerializeToJsonObject(config).ContainsKey(nameof(AiAgentConfig.LiveMcpToken)), Is.False);
    }
}
