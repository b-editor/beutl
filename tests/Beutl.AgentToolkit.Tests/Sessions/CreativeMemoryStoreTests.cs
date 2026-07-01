using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class CreativeMemoryStoreTests
{
    private static string TempRoot()
        => Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));

    [Test]
    public void Creative_memory_round_trips_deduplicates_and_caps_recent_fingerprints()
    {
        string workspace = TempRoot();
        string global = TempRoot();
        var writer = new CreativeMemoryStore(workspace, capacity: 2, globalRoot: global);

        writer.Record(new CreativeDirectionFingerprint(
            "First",
            ["paper", "red accent"],
            ["fold"],
            "stack",
            DateTimeOffset.UtcNow.AddMinutes(-2)));
        writer.Record(new CreativeDirectionFingerprint(
            "Second",
            ["glass"],
            ["slide"],
            "split",
            DateTimeOffset.UtcNow.AddMinutes(-1)));
        writer.Record(new CreativeDirectionFingerprint(
            "First",
            ["paper"],
            ["settle"],
            "stack",
            DateTimeOffset.UtcNow));

        var reader = new CreativeMemoryStore(workspace, capacity: 2, globalRoot: global);
        IReadOnlyList<CreativeDirectionFingerprint> recent = reader.ReadRecent();

        Assert.Multiple(() =>
        {
            Assert.That(recent, Has.Count.EqualTo(2));
            Assert.That(recent[0].ConceptLabel, Is.EqualTo("First"));
            Assert.That(recent[0].MotionVerbs, Is.EqualTo(new[] { "settle" }));
            Assert.That(recent[1].ConceptLabel, Is.EqualTo("Second"));
        });
    }

    [Test]
    public void Fingerprint_recorded_in_one_workspace_is_visible_from_another_via_global_layer()
    {
        string global = TempRoot();
        string workspaceA = TempRoot();
        string workspaceB = TempRoot();

        var storeA = new CreativeMemoryStore(workspaceA, globalRoot: global);
        storeA.Record(new CreativeDirectionFingerprint(
            "Origami",
            ["paper", "red accent"],
            ["fold"],
            "stack",
            DateTimeOffset.UtcNow));

        // A fresh project (different workspace root) still sees the direction through the shared global layer.
        var storeB = new CreativeMemoryStore(workspaceB, globalRoot: global);
        IReadOnlyList<CreativeDirectionFingerprint> recent = storeB.ReadRecent();

        Assert.That(recent.Select(item => item.ConceptLabel), Has.Member("Origami"));
    }

    [Test]
    public void Recent_unions_workspace_and_global_without_duplicating_shared_directions()
    {
        string global = TempRoot();
        string workspaceA = TempRoot();
        string workspaceB = TempRoot();

        var storeA = new CreativeMemoryStore(workspaceA, globalRoot: global);
        storeA.Record(new CreativeDirectionFingerprint(
            "Shared",
            ["glass"],
            ["slide"],
            "split",
            DateTimeOffset.UtcNow.AddMinutes(-2)));

        var storeB = new CreativeMemoryStore(workspaceB, globalRoot: global);
        // Same direction re-produced in another project, plus a workspace-local-only one.
        storeB.Record(new CreativeDirectionFingerprint(
            "Shared",
            ["glass"],
            ["settle"],
            "split",
            DateTimeOffset.UtcNow.AddMinutes(-1)));
        storeB.Record(new CreativeDirectionFingerprint(
            "LocalOnly",
            ["ink"],
            ["drift"],
            "grid",
            DateTimeOffset.UtcNow));

        IReadOnlyList<CreativeDirectionFingerprint> recent = storeB.ReadRecent();
        List<CreativeDirectionFingerprint> shared =
            recent.Where(item => item.ConceptLabel == "Shared").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(recent.Select(item => item.ConceptLabel), Is.EquivalentTo(new[] { "LocalOnly", "Shared" }));
            Assert.That(shared, Has.Count.EqualTo(1), "shared direction must be deduped across the union");
            Assert.That(shared[0].MotionVerbs, Is.EqualTo(new[] { "settle" }), "the freshest occurrence wins");
        });
    }
}
