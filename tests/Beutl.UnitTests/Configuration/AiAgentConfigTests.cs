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
            Assert.That(config.AgentRoot, Is.Empty);
            Assert.That(config.WorkspaceRoot, Is.Empty);
            Assert.That(config.InstallLayout, Is.Empty);
            Assert.That(config.SkillsDirectory, Is.Empty);
            Assert.That(config.SubagentsDirectory, Is.Empty);
            Assert.That(config.InstallSkills, Is.True);
            Assert.That(config.InstallSubagents, Is.True);
            Assert.That(config.InstallStdioMcp, Is.True);
            Assert.That(config.InstallLiveMcp, Is.False);
            Assert.That(config.McpConfigFileName, Is.EqualTo(".mcp.json"));
            Assert.That(config.McpServersPropertyName, Is.EqualTo("servers"));
        });
    }

    [Test]
    public void Serialization_roundtrips_all_values()
    {
        var source = new AiAgentConfig
        {
            AgentRoot = "/repo",
            WorkspaceRoot = "/videos",
            InstallLayout = "ClaudeCode",
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
            Assert.That(restored.AgentRoot, Is.EqualTo(source.AgentRoot));
            Assert.That(restored.WorkspaceRoot, Is.EqualTo(source.WorkspaceRoot));
            Assert.That(restored.InstallLayout, Is.EqualTo(source.InstallLayout));
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

        config.AgentRoot = "/repo";

        Assert.That(raised, Is.EqualTo(1));
    }
}
