using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

public sealed class AgentToolkitInstallerTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "agent-toolkit-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Test]
    public void BundledAssets_LoadsSkillsAndSubagents()
    {
        IReadOnlyList<AgentToolkitAsset> assets = BundledAgentToolkitAssets.Load();

        Assert.That(assets, Has.Count.EqualTo(9));
        Assert.That(assets.Count(x => x.Kind == AgentToolkitAssetKind.Skill), Is.EqualTo(6));
        Assert.That(assets.Count(x => x.Kind == AgentToolkitAssetKind.Subagent), Is.EqualTo(3));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-brief-expansion/SKILL.md"));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-timeline-from-shotlist/SKILL.md"));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-asset-sourcing/SKILL.md"));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-source-grounding/SKILL.md"));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-visual-review/SKILL.md"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("evaluate_motion_variation"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("insert-new-animated-text-keyframes"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("derive_palette"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("Contrast Exemplars - derive, don't copy"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("subdivisionLevel: 1"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("cutContinuityActuals"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-look-effect-chain/SKILL.md").Content,
            Does.Contain("get_background_grammar"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-look-effect-chain/SKILL.md").Content,
            Does.Contain("Unjustified choices are disallowed"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-asset-sourcing/SKILL.md").Content,
            Does.Contain("assets/manifest.json"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-asset-sourcing/SKILL.md").Content,
            Does.Contain("CC-BY-SA"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-source-grounding/SKILL.md").Content,
            Does.Contain("measure_object_bounds"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-visual-review/SKILL.md").Content,
            Does.Contain("paletteHarmony"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-visual-review/SKILL.md").Content,
            Does.Contain("subdivisionLevel:1"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-visual-review/SKILL.md").Content,
            Does.Contain("Convergence Loop Mode"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-brief-expansion/SKILL.md").Content,
            Does.Contain("expandedBrief"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-brief-expansion/SKILL.md").Content,
            Does.Contain("direction-only"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-brief-expansion/SKILL.md").Content,
            Does.Contain("recentToAvoid"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-builder.md").Content,
            Does.Contain("evaluate_motion_variation"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-builder.md").Content,
            Does.Contain("animate-float-property-keyframes"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-builder.md").Content,
            Does.Contain("subdivisionLevel:1"));
        Assert.That(assets.Single(x => x.RelativePath == "beutl-agent-look-applier.md").Content, Does.Contain("render_still"));
        Assert.That(assets.Single(x => x.RelativePath == "beutl-agent-quality-reviewer.md").Content, Does.Contain("final_preflight"));
        Assert.That(assets.Single(x => x.RelativePath == "beutl-agent-quality-reviewer.md").Content, Does.Contain("subdivisionLevel:1"));
    }

    [Test]
    public async Task InstallAsync_WritesDefaultDirectoriesAndMergesMcpConfig()
    {
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              // user-managed server must stay
              "mcpServers": {
                "existing": { "type": "stdio", "command": "other" }
              },
              "otherSetting": true
            }
            """);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                WorkspaceRoot = Path.Combine(_tempRoot, "workspace"),
                StdioMcpCommand = "dotnet",
                StdioMcpArguments = ["run", "--project", "server.csproj"],
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "agent"),
            ]);

        Assert.That(File.ReadAllText(Path.Combine(_tempRoot, "skills", "demo", "SKILL.md")), Is.EqualTo("skill"));
        Assert.That(File.ReadAllText(Path.Combine(_tempRoot, "agents", "demo.md")), Is.EqualTo("agent"));
        Assert.That(result.McpConfigPath, Is.EqualTo(configPath));

        JsonObject root = ReadJson(configPath);
        JsonObject servers = root["mcpServers"]!.AsObject();
        Assert.That(servers["existing"], Is.Not.Null);

        JsonObject beutlServer = servers["beutl-agent"]!.AsObject();
        Assert.That(beutlServer["type"], Is.Null);
        Assert.That(beutlServer["command"]!.GetValue<string>(), Is.EqualTo("dotnet"));
        Assert.That(beutlServer["args"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(),
            Is.EqualTo(new[] { "run", "--project", "server.csproj" }));
        Assert.That(beutlServer["env"]!.AsObject()["BEUTL_WORKSPACE"]!.GetValue<string>(),
            Is.EqualTo(Path.GetFullPath(Path.Combine(_tempRoot, "workspace"))));
        Assert.That(root["otherSetting"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task InstallAsync_WritesCatalogDirectoriesAndLiveMcpConfig()
    {
        Uri liveUri = new("http://127.0.0.1:12345/mcp?token=secret");
        AgentDefinition claudeCode = AgentCatalog.Find("claude-code")!;

        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                SkillsDirectory = claudeCode.SkillsDirectory(AgentInstallScope.Project),
                SubagentsDirectory = claudeCode.SubagentsDirectory(AgentInstallScope.Project)!,
                InstallStdioMcp = false,
                InstallLiveMcp = true,
                LiveMcpUri = liveUri,
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "agent"),
            ]);

        Assert.That(File.Exists(Path.Combine(_tempRoot, ".claude", "skills", "demo", "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempRoot, ".claude", "agents", "demo.md")), Is.True);

        JsonObject servers = ReadJson(Path.Combine(_tempRoot, ".mcp.json"))["mcpServers"]!.AsObject();
        JsonObject liveServer = servers["beutl-live"]!.AsObject();
        Assert.That(liveServer["type"]!.GetValue<string>(), Is.EqualTo("http"));
        Assert.That(liveServer["url"]!.GetValue<string>(), Is.EqualTo(liveUri.ToString()));
    }

    [Test]
    public async Task InstallAsync_WritesAgentSpecificEntryShapes()
    {
        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                InstallLiveMcp = true,
                McpConfigFileName = Path.Combine(".gemini", "settings.json"),
                StdioMcpCommand = "beutl-mcp",
                StdioMcpTypeValue = "stdio",
                LiveMcpUrlPropertyName = "httpUrl",
                LiveMcpTypeValue = null,
                LiveMcpUri = new Uri("http://127.0.0.1:5008/mcp?token=abc"),
            },
            []);

        JsonObject servers = ReadJson(Path.Combine(_tempRoot, ".gemini", "settings.json"))["mcpServers"]!.AsObject();
        JsonObject stdioServer = servers["beutl-agent"]!.AsObject();
        JsonObject liveServer = servers["beutl-live"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(stdioServer["type"]!.GetValue<string>(), Is.EqualTo("stdio"));
            Assert.That(liveServer["type"], Is.Null);
            Assert.That(liveServer["url"], Is.Null);
            Assert.That(liveServer["httpUrl"]!.GetValue<string>(), Does.StartWith("http://127.0.0.1:5008/mcp"));
        });
    }

    [Test]
    public async Task InstallAsync_ConvertsSubagentsToCodexToml()
    {
        const string markdown = """
            ---
            name: beutl-agent-demo
            description: Demo subagent.
            ---
            Body line.
            """;

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                SubagentsDirectory = Path.Combine(".codex", "agents"),
                SubagentFormat = SubagentFileFormat.CodexToml,
                InstallSkills = false,
                InstallStdioMcp = false,
            },
            [new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "beutl-agent-demo.md", markdown)]);

        string tomlPath = Path.Combine(_tempRoot, ".codex", "agents", "beutl-agent-demo.toml");
        Assert.Multiple(() =>
        {
            Assert.That(result.InstalledFiles, Is.EqualTo(new[] { tomlPath }));
            string toml = File.ReadAllText(tomlPath);
            Assert.That(toml, Does.StartWith("name = \"beutl-agent-demo\""));
            Assert.That(toml, Does.Contain("description = \"Demo subagent.\""));
            Assert.That(toml, Does.Contain("developer_instructions = '''"));
            Assert.That(toml, Does.Contain("Body line."));
        });
    }

    [Test]
    public async Task InstallAsync_UsesCustomAssetDirectories()
    {
        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                SkillsDirectory = Path.Combine("custom", "skill-pack"),
                SubagentsDirectory = Path.Combine("custom", "agent-pack"),
                InstallStdioMcp = false,
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "agent"),
            ]);

        Assert.That(File.Exists(Path.Combine(_tempRoot, "custom", "skill-pack", "demo", "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempRoot, "custom", "agent-pack", "demo.md")), Is.True);
    }

    [Test]
    public async Task InstallAsync_UsesCustomMcpServersPropertyName()
    {
        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpServersPropertyName = "servers",
                StdioMcpCommand = "beutl-agent",
            },
            []);

        JsonObject root = ReadJson(Path.Combine(_tempRoot, ".mcp.json"));
        Assert.That(root["mcpServers"], Is.Null);
        Assert.That(root["servers"]!.AsObject()["beutl-agent"], Is.Not.Null);
    }

    [Test]
    public void InstallAsync_RejectsAssetPathsOutsideAgentRoot()
    {
        Assert.ThrowsAsync<ArgumentException>(() => AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallStdioMcp = false,
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "../escape.md", "bad"),
            ]));
    }

    [Test]
    public void InstallAsync_RejectsMcpConfigPathsOutsideAgentRoot()
    {
        Assert.ThrowsAsync<ArgumentException>(() => AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpConfigFileName = "../.mcp.json",
                StdioMcpCommand = "dotnet",
            },
            []));
    }

    private static JsonObject ReadJson(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
