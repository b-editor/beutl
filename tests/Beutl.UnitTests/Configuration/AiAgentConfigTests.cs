using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class AiAgentConfigTests
{
    [Test]
    public void Defaults_are_install_everything_but_live_mcp()
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
            Assert.That(config.InstallStdioMcp, Is.True);
            Assert.That(config.InstallLiveMcp, Is.False);
            Assert.That(config.McpConfigFileName, Is.Empty);
            Assert.That(config.McpServersPropertyName, Is.Empty);
        });
    }

    [Test]
    public void Serialization_roundtrips_all_values()
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
}
