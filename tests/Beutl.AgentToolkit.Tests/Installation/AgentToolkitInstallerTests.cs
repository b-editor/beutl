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

        Assert.That(assets, Has.Count.EqualTo(4));
        Assert.That(assets.Count(x => x.Kind == AgentToolkitAssetKind.Skill), Is.EqualTo(2));
        Assert.That(assets.Count(x => x.Kind == AgentToolkitAssetKind.Subagent), Is.EqualTo(2));
        Assert.That(assets.Select(x => x.RelativePath), Does.Contain("beutl-agent-timeline-from-shotlist/SKILL.md"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("evaluate_motion_variation"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-from-shotlist/SKILL.md").Content,
            Does.Contain("insert-new-animated-text-keyframes"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-builder.md").Content,
            Does.Contain("evaluate_motion_variation"));
        Assert.That(
            assets.Single(x => x.RelativePath == "beutl-agent-timeline-builder.md").Content,
            Does.Contain("animate-float-property-keyframes"));
        Assert.That(assets.Single(x => x.RelativePath == "beutl-agent-look-applier.md").Content, Does.Contain("render_still"));
    }

    [Test]
    public async Task InstallAsync_WritesGenericLayoutAndMergesMcpConfig()
    {
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              // user-managed server must stay
              "servers": {
                "existing": { "type": "stdio", "command": "other" }
              },
              "otherSetting": true
            }
            """);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                Layout = AgentToolkitInstallLayout.Generic,
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
        JsonObject servers = root["servers"]!.AsObject();
        Assert.That(servers["existing"], Is.Not.Null);

        JsonObject beutlServer = servers["beutl-agent"]!.AsObject();
        Assert.That(beutlServer["type"]!.GetValue<string>(), Is.EqualTo("stdio"));
        Assert.That(beutlServer["command"]!.GetValue<string>(), Is.EqualTo("dotnet"));
        Assert.That(beutlServer["args"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(),
            Is.EqualTo(new[] { "run", "--project", "server.csproj" }));
        Assert.That(beutlServer["env"]!.AsObject()["BEUTL_WORKSPACE"]!.GetValue<string>(),
            Is.EqualTo(Path.GetFullPath(Path.Combine(_tempRoot, "workspace"))));
        Assert.That(root["otherSetting"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task InstallAsync_WritesClaudeLayoutAndLiveMcpConfig()
    {
        Uri liveUri = new("http://127.0.0.1:12345/mcp?token=secret");

        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                Layout = AgentToolkitInstallLayout.ClaudeCode,
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

        JsonObject servers = ReadJson(Path.Combine(_tempRoot, ".mcp.json"))["servers"]!.AsObject();
        JsonObject liveServer = servers["beutl-live"]!.AsObject();
        Assert.That(liveServer["type"]!.GetValue<string>(), Is.EqualTo("http"));
        Assert.That(liveServer["url"]!.GetValue<string>(), Is.EqualTo(liveUri.ToString()));
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
                McpServersPropertyName = "mcpServers",
                StdioMcpCommand = "beutl-agent",
            },
            []);

        JsonObject root = ReadJson(Path.Combine(_tempRoot, ".mcp.json"));
        Assert.That(root["servers"], Is.Null);
        Assert.That(root["mcpServers"]!.AsObject()["beutl-agent"], Is.Not.Null);
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
