using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class CreativeMemoryStoreTests
{
    [Test]
    public void Creative_memory_round_trips_deduplicates_and_caps_recent_fingerprints()
    {
        string workspace = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var writer = new CreativeMemoryStore(workspace, capacity: 2);

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

        var reader = new CreativeMemoryStore(workspace, capacity: 2);
        IReadOnlyList<CreativeDirectionFingerprint> recent = reader.ReadRecent();

        Assert.Multiple(() =>
        {
            Assert.That(recent, Has.Count.EqualTo(2));
            Assert.That(recent[0].ConceptLabel, Is.EqualTo("First"));
            Assert.That(recent[0].MotionVerbs, Is.EqualTo(new[] { "settle" }));
            Assert.That(recent[1].ConceptLabel, Is.EqualTo("Second"));
        });
    }
}
