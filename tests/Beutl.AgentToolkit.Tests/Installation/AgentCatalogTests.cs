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
            // ~/.claude.json is app-managed state; user scope goes through `claude mcp add`.
            Assert.That(agent.Mcp(AgentInstallScope.Global), Is.Null);
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
            Assert.That(AgentCatalog.Find("hermes-agent")!.GlobalMcp, Is.Null);
            Assert.That(AgentCatalog.Find("trae")!.ProjectMcp, Is.Null);
            Assert.That(AgentCatalog.Find("zed")!.GlobalMcp, Is.Null);
            Assert.That(AgentCatalog.Find("cline")!.GlobalMcp, Is.Null);
        });
    }

    [Test]
    public void Verified_mcp_locations_match_vendor_documentation()
    {
        Assert.Multiple(() =>
        {
            AgentMcpLocation warp = AgentCatalog.Find("warp")!.GlobalMcp!;
            Assert.That(warp.ConfigFileName, Is.EqualTo(Path.Combine(".warp", ".mcp.json")));
            Assert.That(warp.ServersPropertyName, Is.EqualTo("mcpServers"));

            AgentMcpLocation junie = AgentCatalog.Find("junie")!.ProjectMcp!;
            Assert.That(junie.ConfigFileName, Is.EqualTo(Path.Combine(".junie", "mcp", "mcp.json")));

            AgentMcpLocation droid = AgentCatalog.Find("droid")!.GlobalMcp!;
            Assert.That(droid.ConfigFileName, Is.EqualTo(Path.Combine(".factory", "mcp.json")));

            AgentMcpLocation openHands = AgentCatalog.Find("openhands")!.GlobalMcp!;
            Assert.That(openHands.ConfigFileName, Is.EqualTo(Path.Combine(".openhands", "mcp.json")));
            Assert.That(openHands.RemoteUrlPropertyName, Is.Null);

            AgentMcpLocation crush = AgentCatalog.Find("crush")!.ProjectMcp!;
            Assert.That(crush.ServersPropertyName, Is.EqualTo("mcp"));
            Assert.That(crush.StdioTypeValue, Is.EqualTo("stdio"));

            AgentMcpLocation gemini = AgentCatalog.Find("gemini-cli")!.ProjectMcp!;
            Assert.That(gemini.RemoteUrlPropertyName, Is.EqualTo("httpUrl"));
            Assert.That(gemini.RemoteTypeValue, Is.Null);

            AgentMcpLocation windsurf = AgentCatalog.Find("windsurf")!.GlobalMcp!;
            Assert.That(windsurf.RemoteUrlPropertyName, Is.EqualTo("serverUrl"));

            AgentMcpLocation amp = AgentCatalog.Find("amp")!.ProjectMcp!;
            Assert.That(amp.ConfigFileName, Is.EqualTo(Path.Combine(".amp", "settings.json")));
            Assert.That(amp.ServersPropertyName, Is.EqualTo("amp.mcpServers"));
        });
    }

    [Test]
    public void Codex_uses_the_dot_agents_skills_convention_in_both_scopes()
    {
        AgentDefinition agent = AgentCatalog.Find("codex")!;

        Assert.Multiple(() =>
        {
            Assert.That(
                agent.SkillsDirectory(AgentInstallScope.Project),
                Is.EqualTo(Path.Combine(".agents", "skills")));
            Assert.That(
                agent.SkillsDirectory(AgentInstallScope.Global),
                Is.EqualTo(Path.Combine(".agents", "skills")));
            // Codex subagents are TOML (.codex/agents), incompatible with the
            // markdown subagents this toolkit ships.
            Assert.That(agent.SubagentsDirectory(AgentInstallScope.Project), Is.Null);
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
