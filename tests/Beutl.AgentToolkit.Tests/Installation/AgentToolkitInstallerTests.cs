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
                InstallMcp = false,
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
                McpCommand = "dotnet",
                McpArguments = ["run", "--project", "server.csproj"],
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "agent"),
            ]);

        Assert.That(File.ReadAllText(Path.Combine(_tempRoot, "skills", "demo", "SKILL.md")), Is.EqualTo("skill"));
        Assert.That(File.ReadAllText(Path.Combine(_tempRoot, "agents", "demo.md")), Is.EqualTo("agent"));
        Assert.That(result.InstalledFiles, Does.Contain(configPath));
        Assert.That(result.InstalledMcp, Is.True);

        JsonObject root = ReadJson(configPath);
        JsonObject servers = root["mcpServers"]!.AsObject();
        Assert.That(servers["existing"], Is.Not.Null);

        JsonObject beutlServer = servers["beutl-agent"]!.AsObject();
        Assert.That(beutlServer["type"], Is.Null);
        Assert.That(beutlServer["command"]!.GetValue<string>(), Is.EqualTo("dotnet"));
        Assert.That(beutlServer["args"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(),
            Is.EqualTo(new[] { "run", "--project", "server.csproj" }));
        Assert.That(beutlServer["env"], Is.Null);
        Assert.That(root["otherSetting"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public async Task InstallAsync_RemovesTheLiveEntryOfEarlierVersions()
    {
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "mcpServers": {
                "existing": { "type": "stdio", "command": "other" },
                "beutl-agent": { "command": "old", "args": [], "env": { "BEUTL_WORKSPACE": "/old" } },
                "beutl-live": { "type": "http", "url": "http://127.0.0.1:59737/mcp", "headers": { "Authorization": "Bearer old-token" } }
              }
            }
            """);

        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpCommand = "beutl-mcp",
            },
            []);

        string json = await File.ReadAllTextAsync(configPath);
        JsonObject servers = ReadJson(configPath)["mcpServers"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(servers.Select(pair => pair.Key), Is.EquivalentTo(new[] { "existing", "beutl-agent" }));
            Assert.That(servers["beutl-agent"]!["command"]!.GetValue<string>(), Is.EqualTo("beutl-mcp"));
            Assert.That(json, Does.Not.Contain("old-token"));
            Assert.That(json, Does.Not.Contain("BEUTL_WORKSPACE"));
        });
    }

    [Test]
    public async Task InstallAsync_WritesCatalogDirectoriesAndOneProfileBoundServer()
    {
        AgentDefinition claudeCode = AgentCatalog.Agents.Single(a => a.Id == "claude-code");

        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                SkillsDirectory = claudeCode.SkillsDirectory(AgentInstallScope.Project),
                SubagentsDirectory = claudeCode.SubagentsDirectory(AgentInstallScope.Project)!,
                McpCommand = "/opt/beutl/Beutl.AgentToolkit.Mcp",
                McpEnvironment = new Dictionary<string, string> { ["BEUTL_HOME"] = "/home/user/.beutl" },
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "agent"),
            ]);

        Assert.That(File.Exists(Path.Combine(_tempRoot, ".claude", "skills", "demo", "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempRoot, ".claude", "agents", "demo.md")), Is.True);

        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        JsonObject servers = ReadJson(configPath)["mcpServers"]!.AsObject();
        JsonObject server = servers["beutl-agent"]!.AsObject();
        Assert.Multiple(() =>
        {
            // One stdio entry serves live and headless editing. It finds and authenticates to the
            // running editors through the profile, so the config holds no URL and no bearer token.
            Assert.That(servers.Select(pair => pair.Key), Is.EqualTo(new[] { "beutl-agent" }));
            Assert.That(server["type"], Is.Null);
            Assert.That(server["command"]!.GetValue<string>(), Is.EqualTo("/opt/beutl/Beutl.AgentToolkit.Mcp"));
            Assert.That(server["args"]!.AsArray(), Is.Empty);
            Assert.That(server["env"]!.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "BEUTL_HOME" }));
            Assert.That(server["env"]!.AsObject()["BEUTL_HOME"]!.GetValue<string>(), Is.EqualTo("/home/user/.beutl"));
            Assert.That(server["url"], Is.Null);
            Assert.That(server["headers"], Is.Null);
            Assert.That(File.ReadAllText(configPath), Does.Not.Contain("Bearer").And.Not.Contain("127.0.0.1"));
        });
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
                McpConfigFileName = Path.Combine(".gemini", "settings.json"),
                McpCommand = "beutl-mcp",
                McpTypeValue = "stdio",
            },
            []);

        JsonObject servers = ReadJson(Path.Combine(_tempRoot, ".gemini", "settings.json"))["mcpServers"]!.AsObject();
        JsonObject server = servers["beutl-agent"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(server["type"]!.GetValue<string>(), Is.EqualTo("stdio"));
            Assert.That(server["command"]!.GetValue<string>(), Is.EqualTo("beutl-mcp"));
            Assert.That(server["env"], Is.Null);
        });
    }

    [TestCase(McpConfigFormat.Json, ".mcp.json")]
    [TestCase(McpConfigFormat.CodexToml, "config.toml")]
    public async Task InstallAsync_RequiresTheServerCommand(McpConfigFormat format, string configFileName)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpConfigFileName = configFileName,
                McpConfigFormat = format,
            },
            []));
        Assert.That(File.Exists(Path.Combine(_tempRoot, configFileName)), Is.False);
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
                InstallMcp = false,
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
                InstallMcp = false,
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
                McpCommand = "beutl-agent",
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
                McpCommand = "beutl-agent",
            },
            []);

        JsonObject root = ReadJson(Path.Combine(_tempRoot, ".mcp.json"));
        Assert.That(root["amp.mcpServers"], Is.Null, "The dotted name must not be written as a single flat key.");
        JsonObject amp = root["amp"]!.AsObject();
        Assert.That(amp["mcpServers"]!.AsObject()["beutl-agent"], Is.Not.Null);
    }

    [Test]
    public async Task InstallAsync_RejectsAssetPathsOutsideAgentRoot()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallMcp = false,
            },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "../escape.md", "bad"),
            ]));
    }

    [Test]
    public async Task InstallAsync_RejectsMcpConfigPathsOutsideAgentRoot()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpConfigFileName = "../.mcp.json",
                McpCommand = "dotnet",
            },
            []));
    }

    // Writing a JSON file back drops its comments, so a file with comments is left as it is and the
    // install returns the entry to add by hand.
    [Test]
    public async Task InstallAsync_LeavesAConfigWithCommentsAsItIsAndReturnsTheEntryToAdd()
    {
        string configPath = Path.Combine(_tempRoot, ".gemini", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        const string original = """
            {
              // keep me
              "theme": "dark",
              "mcpServers": {
                "beutl-live": { "httpUrl": "http://127.0.0.1:59737/mcp" }
              }
            }
            """;
        await File.WriteAllTextAsync(configPath, original);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpConfigFileName = Path.Combine(".gemini", "settings.json"),
                McpCommand = "beutl-mcp",
            },
            []);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(original));
            Assert.That(result.InstalledMcp, Is.False);
            Assert.That(result.InstalledFiles, Is.Empty);
            Assert.That(result.ManualMcpEdit, Is.Not.Null);
            Assert.That(result.ManualMcpEdit!.ConfigPath, Is.EqualTo(configPath));
            Assert.That(result.ManualMcpEdit.ObsoleteEntries, Is.EqualTo(new[] { "beutl-live" }));
            JsonObject entry = JsonNode.Parse(result.ManualMcpEdit.Snippet)!["mcpServers"]!["beutl-agent"]!.AsObject();
            Assert.That(entry["command"]!.GetValue<string>(), Is.EqualTo("beutl-mcp"));
        });
    }

    // A reinstall that changes nothing leaves the file alone, comments and formatting included.
    [TestCase(true)]
    [TestCase(false)]
    public async Task InstallAsync_DoesNotRewriteAConfigThatAlreadyHasTheEntry(bool withComment)
    {
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        string original = (withComment ? "// managed by hand\n" : "")
                          + "{\n    \"mcpServers\": {\n        \"beutl-agent\": { \"command\": \"beutl-mcp\", \"args\": [ \"mcp\" ] }\n    }\n}\n";
        await File.WriteAllTextAsync(configPath, original);

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpCommand = "beutl-mcp",
                McpArguments = ["mcp"],
            },
            []);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(original));
            Assert.That(result.InstalledMcp, Is.True);
            Assert.That(result.ManualMcpEdit, Is.Null);
            Assert.That(result.InstalledFiles, Is.EqualTo(new[] { configPath }));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task JsonMcpConfig_failed_or_cancelled_writes_preserve_the_original_and_remove_temporary_files(
        bool exists,
        bool cancel)
    {
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        const string original = "{\n  \"mcpServers\": {\n    \"other\": { \"command\": \"keep this\" }\n  }\n}\n";
        if (exists)
        {
            await File.WriteAllTextAsync(configPath, original);
        }

        using var cancellation = new CancellationTokenSource();
        Task Write() => JsonMcpConfigWriter.WriteAsync(
            configPath,
            new AgentToolkitInstallOptions { AgentRoot = _tempRoot, McpCommand = "beutl-mcp" },
            cancellation.Token,
            async (stream, _, token) =>
            {
                await stream.WriteAsync("partial"u8.ToArray(), CancellationToken.None);
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                throw new IOException("Simulated write failure.");
            });

        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(Write);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(Write);
        }

        Assert.That(File.Exists(configPath), Is.EqualTo(exists));
        if (exists)
        {
            Assert.That(await File.ReadAllTextAsync(configPath), Is.EqualTo(original));
        }

        Assert.That(Directory.EnumerateFiles(_tempRoot, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task InstallAsync_KeepsAJsonConfigSymlinkAndItsPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Creating symlinks requires additional Windows privileges.");
            return;
        }

        string target = Path.Combine(_tempRoot, "managed-mcp.json");
        await File.WriteAllTextAsync(target, "{ \"theme\": \"dark\" }");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        string configPath = Path.Combine(_tempRoot, ".mcp.json");
        File.CreateSymbolicLink(configPath, target);

        await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions
            {
                AgentRoot = _tempRoot,
                InstallSkills = false,
                InstallSubagents = false,
                McpCommand = "beutl-mcp",
            },
            []);

        JsonObject root = ReadJson(target);
        UnixFileMode mode = File.GetUnixFileMode(target);
        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(configPath).LinkTarget, Is.EqualTo(target));
            Assert.That(root["theme"]!.GetValue<string>(), Is.EqualTo("dark"));
            Assert.That(root["mcpServers"]!["beutl-agent"], Is.Not.Null);
            Assert.That(mode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
    }

    // The manifest records what an earlier install wrote, so an edit since then is recognized and
    // copied aside before a newer bundled version replaces it; an untouched file is simply replaced.
    [Test]
    public async Task InstallAsync_BacksUpAnEditedSkillBeforeReplacingIt()
    {
        AgentToolkitAsset[] assets =
        [
            new(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "bundled skill"),
            new(AgentToolkitAssetKind.Subagent, "demo.md", "bundled agent"),
        ];
        var options = new AgentToolkitInstallOptions { AgentRoot = _tempRoot, InstallMcp = false };
        AgentToolkitInstallResult first = await AgentToolkitInstaller.InstallAsync(options, assets);
        string skillPath = Path.Combine(_tempRoot, "skills", "demo", "SKILL.md");
        string agentPath = Path.Combine(_tempRoot, "agents", "demo.md");
        await File.WriteAllTextAsync(skillPath, "my edits");

        AgentToolkitInstallResult second = await AgentToolkitInstaller.InstallAsync(
            options with { PreviousFiles = first.AssetFileRecords },
            [
                new(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "updated skill"),
                new(AgentToolkitAssetKind.Subagent, "demo.md", "updated agent"),
            ]);

        Assert.That(second.BackedUpFiles, Has.Count.EqualTo(1));
        BackedUpFile backup = second.BackedUpFiles[0];
        Assert.Multiple(() =>
        {
            Assert.That(backup.Path, Is.EqualTo(skillPath));
            Assert.That(File.ReadAllText(backup.BackupPath), Is.EqualTo("my edits"));
            Assert.That(Path.GetDirectoryName(backup.BackupPath), Is.EqualTo(Path.GetDirectoryName(skillPath)));
            Assert.That(backup.BackupPath, Does.EndWith(".bak"));
            Assert.That(File.ReadAllText(skillPath), Is.EqualTo("updated skill"));
            Assert.That(File.ReadAllText(agentPath), Is.EqualTo("updated agent"));
            Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(agentPath)!, "*.bak"), Is.Empty);
        });
    }

    // A file no install recorded may be the user's own, so it is backed up too unless it already holds
    // the bundled content.
    [Test]
    public async Task InstallAsync_BacksUpADifferentFileThatNoInstallRecorded()
    {
        string skillPath = Path.Combine(_tempRoot, "skills", "demo", "SKILL.md");
        string agentPath = Path.Combine(_tempRoot, "agents", "demo.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(agentPath)!);
        await File.WriteAllTextAsync(skillPath, "a skill of my own");
        await File.WriteAllTextAsync(agentPath, "bundled agent");

        AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
            new AgentToolkitInstallOptions { AgentRoot = _tempRoot, InstallMcp = false },
            [
                new AgentToolkitAsset(AgentToolkitAssetKind.Skill, "demo/SKILL.md", "bundled skill"),
                new AgentToolkitAsset(AgentToolkitAssetKind.Subagent, "demo.md", "bundled agent"),
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(result.BackedUpFiles.Select(file => file.Path), Is.EqualTo(new[] { skillPath }));
            Assert.That(File.ReadAllText(result.BackedUpFiles[0].BackupPath), Is.EqualTo("a skill of my own"));
            Assert.That(File.ReadAllText(skillPath), Is.EqualTo("bundled skill"));
        });
    }

    private static JsonObject ReadJson(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
