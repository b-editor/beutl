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
    public void BundledAssets_LoadsEditingSkillsAndSubagents()
    {
        IReadOnlyList<AgentToolkitAsset> assets = BundledAgentToolkitAssets.Load();
        Assert.Multiple(() =>
        {
            Assert.That(assets.Where(asset => asset.Kind == AgentToolkitAssetKind.Skill).Select(asset => asset.RelativePath),
                Is.EquivalentTo(new[]
                {
                    "beutl-agent-timeline-from-shotlist/SKILL.md", "beutl-agent-look-effect-chain/SKILL.md",
                    "beutl-agent-source-grounding/SKILL.md", "beutl-agent-source-grounding/agents/openai.yaml"
                }));
            Assert.That(assets.Where(asset => asset.Kind == AgentToolkitAssetKind.Subagent).Select(asset => asset.RelativePath),
                Is.EquivalentTo(new[] { "beutl-agent-timeline-builder.md", "beutl-agent-look-applier.md" }));
            Assert.That(assets.All(asset => !string.IsNullOrWhiteSpace(asset.Content)), Is.True);
        });
    }

    [Test]
    public async Task InstallAsync_WritesBundledSkillMetadata()
    {
        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSubagents = false,
                InstallStdioMcp = false,
            },
            BundledAgentToolkitAssets.Load());

        string metadataPath = Path.Combine(
            _tempRoot,
            "skills",
            "beutl-agent-source-grounding",
            "agents",
            "openai.yaml");

        Assert.That(File.Exists(metadataPath), Is.True);
        Assert.That(File.ReadAllText(metadataPath), Does.Contain("Beutl Agent Source Grounding"));
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
        Uri liveUri = new("http://127.0.0.1:12345/mcp");
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
                LiveMcpHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer secret",
                },
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
        Assert.That(liveServer["url"]!.GetValue<string>(), Does.Not.Contain("token"));
        Assert.That(
            liveServer["headers"]!.AsObject()["Authorization"]!.GetValue<string>(),
            Is.EqualTo("Bearer secret"));
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
    public async Task InstallAsync_NestsDottedMcpServersPropertyName()
    {
        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpServersPropertyName = "amp.mcpServers",
                StdioMcpCommand = "beutl-agent",
            },
            []);

        JsonObject root = ReadJson(Path.Combine(_tempRoot, ".mcp.json"));
        Assert.That(root["amp.mcpServers"], Is.Null, "The dotted name must not be written as a single flat key.");
        JsonObject amp = root["amp"]!.AsObject();
        Assert.That(amp["mcpServers"]!.AsObject()["beutl-agent"], Is.Not.Null);
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
