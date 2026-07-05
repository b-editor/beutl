using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

[TestFixture]
public sealed class AgentCatalogTests
{
    [Test]
    public void Agents_have_unique_ids_and_display_names()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentCatalog.Agents, Is.Not.Empty);
            Assert.That(
                AgentCatalog.Agents.Select(a => a.Id).Distinct().Count(),
                Is.EqualTo(AgentCatalog.Agents.Count));
            Assert.That(AgentCatalog.Agents.All(a => a.DisplayName.Length > 0), Is.True);
        });
    }

    [Test]
    public void Every_directory_and_config_path_is_relative_and_traversal_free()
    {
        foreach (AgentDefinition agent in AgentCatalog.Agents)
        {
            string?[] paths =
            [
                agent.ProjectSkillsDirectory,
                agent.GlobalSkillsDirectory,
                agent.ProjectSubagentsDirectory,
                agent.GlobalSubagentsDirectory,
                agent.ProjectMcp?.ConfigFileName,
                agent.GlobalMcp?.ConfigFileName,
            ];

            foreach (string? path in paths.Where(p => p is not null))
            {
                Assert.That(Path.IsPathRooted(path!), Is.False, $"{agent.Id}: {path}");
                Assert.That(path, Does.Not.Contain(".."), $"{agent.Id}: {path}");
            }

            if (agent.ProjectMcp is { } projectMcp)
            {
                Assert.That(projectMcp.ServersPropertyName, Is.Not.Empty, agent.Id);
            }

            if (agent.GlobalMcp is { } globalMcp)
            {
                Assert.That(globalMcp.ServersPropertyName, Is.Not.Empty, agent.Id);
            }
        }
    }

    [Test]
    public void Claude_code_resolves_per_scope_with_subagents_and_mcp()
    {
        AgentDefinition agent = AgentCatalog.Find("claude-code")!;

        Assert.Multiple(() =>
        {
            Assert.That(
                agent.SkillsDirectory(AgentInstallScope.Project),
                Is.EqualTo(Path.Combine(".claude", "skills")));
            Assert.That(
                agent.SubagentsDirectory(AgentInstallScope.Project),
                Is.EqualTo(Path.Combine(".claude", "agents")));
            Assert.That(agent.Mcp(AgentInstallScope.Project)!.ConfigFileName, Is.EqualTo(".mcp.json"));
            Assert.That(agent.Mcp(AgentInstallScope.Project)!.ServersPropertyName, Is.EqualTo("mcpServers"));
            Assert.That(agent.Mcp(AgentInstallScope.Global)!.ConfigFileName, Is.EqualTo(".claude.json"));
        });
    }

    [Test]
    public void Agents_with_non_json_mcp_configs_have_no_mcp_location()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentCatalog.Find("codex")!.ProjectMcp, Is.Null);
            Assert.That(AgentCatalog.Find("codex")!.GlobalMcp, Is.Null);
            Assert.That(AgentCatalog.Find("opencode")!.ProjectMcp, Is.Null);
            Assert.That(AgentCatalog.Find("goose")!.GlobalMcp, Is.Null);
            Assert.That(AgentCatalog.Find("continue")!.ProjectMcp, Is.Null);
        });
    }

    [Test]
    public void Named_agents_from_the_request_are_present()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentCatalog.Find("claude-code"), Is.Not.Null);
            Assert.That(AgentCatalog.Find("codex"), Is.Not.Null);
            Assert.That(AgentCatalog.Find("opencode"), Is.Not.Null);
            Assert.That(AgentCatalog.Find("hermes-agent"), Is.Not.Null);
        });
    }

    [Test]
    public void Find_returns_null_for_unknown_ids()
    {
        Assert.That(AgentCatalog.Find("no-such-agent"), Is.Null);
    }
}
