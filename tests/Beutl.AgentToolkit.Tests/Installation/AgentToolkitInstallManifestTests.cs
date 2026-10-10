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
                    new AgentToolkitInstallManifest("stale", []), assets),
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
