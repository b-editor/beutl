using Beutl.AgentToolkit.Installation;

namespace Beutl.AgentToolkit.Tests.Installation;

[TestFixture]
public sealed class AgentToolkitInstallManifestTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "agent-toolkit-manifest-" + Guid.NewGuid().ToString("N"));
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
    public void Assets_hash_is_stable_across_ordering_and_changes_with_content()
    {
        AgentToolkitAsset[] assets =
        [
            new(AgentToolkitAssetKind.Skill, "a/SKILL.md", "alpha"),
            new(AgentToolkitAssetKind.Subagent, "b.md", "beta"),
        ];

        string hash = AgentToolkitInstallManifestStore.ComputeAssetsHash(assets);
        string reordered = AgentToolkitInstallManifestStore.ComputeAssetsHash(assets.Reverse());
        string changed = AgentToolkitInstallManifestStore.ComputeAssetsHash(
        [
            assets[0] with { Content = "alpha v2" },
            assets[1],
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(reordered, Is.EqualTo(hash));
            Assert.That(changed, Is.Not.EqualTo(hash));
        });
    }

    [Test]
    public void Save_and_load_roundtrip_and_invalid_files_load_as_null()
    {
        string path = Path.Combine(_tempRoot, "manifest.json");
        var manifest = new AgentToolkitInstallManifest(
            "HASH",
            [new InstalledFileRecord("/x/SKILL.md", "ABCD")]);

        AgentToolkitInstallManifestStore.Save(path, manifest);
        AgentToolkitInstallManifest? loaded = AgentToolkitInstallManifestStore.Load(path);

        File.WriteAllText(path, "{not json");
        AgentToolkitInstallManifest? corrupted = AgentToolkitInstallManifestStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.AssetsHash, Is.EqualTo("HASH"));
            Assert.That(loaded.Files, Is.EqualTo(manifest.Files));
            Assert.That(corrupted, Is.Null);
            Assert.That(
                AgentToolkitInstallManifestStore.Load(Path.Combine(_tempRoot, "missing.json")),
                Is.Null);
        });
    }

    [Test]
    public void Update_is_available_only_when_a_manifest_exists_and_hashes_differ()
    {
        AgentToolkitAsset[] assets = [new(AgentToolkitAssetKind.Skill, "a/SKILL.md", "alpha")];
        string hash = AgentToolkitInstallManifestStore.ComputeAssetsHash(assets);

        Assert.Multiple(() =>
        {
            Assert.That(
                AgentToolkitInstallManifestStore.IsUpdateAvailable(null, assets),
                Is.False);
            Assert.That(
                AgentToolkitInstallManifestStore.IsUpdateAvailable(
                    new AgentToolkitInstallManifest(hash, [], AgentToolkitInstallManifest.CurrentMcpLayout), assets),
                Is.False);
            Assert.That(
                AgentToolkitInstallManifestStore.IsUpdateAvailable(
                    new AgentToolkitInstallManifest("stale", [], AgentToolkitInstallManifest.CurrentMcpLayout), assets),
                Is.True);
        });
    }

    [Test]
    public void Manifests_written_before_the_unified_mcp_server_request_a_reinstall()
    {
        AgentToolkitAsset[] assets = [new(AgentToolkitAssetKind.Skill, "a/SKILL.md", "alpha")];
        string hash = AgentToolkitInstallManifestStore.ComputeAssetsHash(assets);
        string path = Path.Combine(_tempRoot, "manifest.json");
        // Written by a version that installed "beutl-agent" next to the "beutl-live" URL entry.
        File.WriteAllText(path, $$"""{ "AssetsHash": "{{hash}}", "Files": [] }""");

        AgentToolkitInstallManifest? legacy = AgentToolkitInstallManifestStore.Load(path);
        AgentToolkitInstallManifestStore.Save(path,
            new AgentToolkitInstallManifest(hash, [], AgentToolkitInstallManifest.CurrentMcpLayout));
        AgentToolkitInstallManifest? current = AgentToolkitInstallManifestStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy!.McpLayout, Is.EqualTo(0));
            Assert.That(AgentToolkitInstallManifestStore.IsMcpLayoutOutdated(legacy), Is.True);
            Assert.That(AgentToolkitInstallManifestStore.IsUpdateAvailable(legacy, assets), Is.True,
                "Matching assets must not hide the MCP layout change.");
            Assert.That(current!.McpLayout, Is.EqualTo(AgentToolkitInstallManifest.CurrentMcpLayout));
            Assert.That(AgentToolkitInstallManifestStore.IsMcpLayoutOutdated(current), Is.False);
            Assert.That(AgentToolkitInstallManifestStore.IsUpdateAvailable(current, assets), Is.False);
            Assert.That(AgentToolkitInstallManifestStore.IsMcpLayoutOutdated(null), Is.False);
        });
    }

    [Test]
    public void Manifests_written_before_targets_were_recorded_have_none()
    {
        string path = Path.Combine(_tempRoot, "manifest.json");
        File.WriteAllText(path, """{ "AssetsHash": "a", "Files": [], "McpLayout": 1 }""");

        AgentToolkitInstallManifest? manifest = AgentToolkitInstallManifestStore.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(manifest!.McpTargets, Is.Null);
            Assert.That(AgentToolkitInstallManifestStore.GetOutdatedMcpTargets(manifest), Is.Empty);
            Assert.That(AgentToolkitInstallManifestStore.UpdateMcpTargets(manifest.McpTargets, null), Is.Empty);
        });
    }

    [Test]
    public void Recorded_targets_survive_a_save_and_load()
    {
        string path = Path.Combine(_tempRoot, "manifest.json");
        InstalledMcpTarget[] targets =
        [
            new(Path.Combine(_tempRoot, ".mcp.json"), IsConfigFile: true, 1),
            new("Claude Code (Global)", IsConfigFile: false, 1),
        ];

        AgentToolkitInstallManifestStore.Save(path, new AgentToolkitInstallManifest("a", [], 1, targets));

        Assert.That(AgentToolkitInstallManifestStore.Load(path)!.McpTargets, Is.EqualTo(targets));
    }

    [Test]
    public void Outdated_targets_are_the_earlier_layouts_that_can_still_be_reinstalled()
    {
        string file = Path.Combine(_tempRoot, ".mcp.json");
        File.WriteAllText(file, "{}");
        var oldFile = new InstalledMcpTarget(file, IsConfigFile: true, 0);
        var oldCli = new InstalledMcpTarget("Claude Code (Global)", IsConfigFile: false, 0);
        var goneFile = new InstalledMcpTarget(Path.Combine(_tempRoot, "gone.json"), IsConfigFile: true, 0);
        var current = new InstalledMcpTarget(Path.Combine(_tempRoot, "current.json"), IsConfigFile: true, AgentToolkitInstallManifest.CurrentMcpLayout);
        File.WriteAllText(current.Location, "{}");

        IReadOnlyList<InstalledMcpTarget> outdated = AgentToolkitInstallManifestStore.GetOutdatedMcpTargets(
            new AgentToolkitInstallManifest("a", [], 0, [oldFile, oldCli, goneFile, current]));

        Assert.That(outdated, Is.EqualTo(new[] { oldFile, oldCli }));
    }

    [Test]
    public void An_install_replaces_its_own_record_and_keeps_the_others()
    {
        string file = Path.Combine(_tempRoot, ".mcp.json");
        string other = Path.Combine(_tempRoot, "other.json");
        File.WriteAllText(file, "{}");
        File.WriteAllText(other, "{}");
        var old = new InstalledMcpTarget(file, IsConfigFile: true, 0);
        var otherOld = new InstalledMcpTarget(other, IsConfigFile: true, 0);
        var goneFile = new InstalledMcpTarget(Path.Combine(_tempRoot, "gone.json"), IsConfigFile: true, 0);
        var cli = new InstalledMcpTarget("Claude Code (Global)", IsConfigFile: false, 0);
        var installed = old with { Layout = AgentToolkitInstallManifest.CurrentMcpLayout };

        Assert.Multiple(() =>
        {
            Assert.That(
                AgentToolkitInstallManifestStore.UpdateMcpTargets([old, otherOld, goneFile, cli], installed),
                Is.EquivalentTo(new[] { installed, otherOld, cli }));
            Assert.That(
                AgentToolkitInstallManifestStore.UpdateMcpTargets([old, otherOld, goneFile, cli], null),
                Is.EquivalentTo(new[] { old, otherOld, cli }), "An install that wrote no entry changes no record.");
        });
    }

    [Test]
    public void Stale_files_are_deleted_only_when_unmodified_and_no_longer_current()
    {
        string staleDir = Path.Combine(_tempRoot, "old-skill");
        Directory.CreateDirectory(staleDir);
        string stalePath = Path.Combine(staleDir, "SKILL.md");
        File.WriteAllText(stalePath, "old content");

        string editedPath = Path.Combine(_tempRoot, "edited.md");
        File.WriteAllText(editedPath, "user changed this");

        string currentPath = Path.Combine(_tempRoot, "current.md");
        File.WriteAllText(currentPath, "current");

        InstalledFileRecord[] previous =
        [
            new(stalePath, AgentToolkitInstallManifestStore.ComputeContentHash("old content")),
            new(editedPath, AgentToolkitInstallManifestStore.ComputeContentHash("original content")),
            new(currentPath, AgentToolkitInstallManifestStore.ComputeContentHash("current")),
        ];

        IReadOnlyList<string> deleted = AgentToolkitInstallManifestStore.RemoveStaleFiles(
            previous,
            new HashSet<string>(
                [currentPath],
                AgentToolkitInstallManifestStore.PathComparer));

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.EqualTo(new[] { stalePath }));
            Assert.That(File.Exists(stalePath), Is.False);
            Assert.That(Directory.Exists(staleDir), Is.False);
            Assert.That(File.Exists(editedPath), Is.True);
            Assert.That(File.Exists(currentPath), Is.True);
        });
    }
}
