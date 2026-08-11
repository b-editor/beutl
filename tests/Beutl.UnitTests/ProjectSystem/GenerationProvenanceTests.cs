using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class GenerationProvenanceTests
{
    [Test]
    public void MultipleProducersAndUnknownSchema_RoundTripWithoutInterpretingPayload()
    {
        JsonElement futurePayload = JsonSerializer.SerializeToElement(new
        {
            futureField = new { nested = new[] { 1, 2, 3 } },
        });
        var element = new Element
        {
            Provenance =
            [
                Create("beutl.ai", "video.generate", 1, new { durationSeconds = "6" }),
                new GenerationProvenance(
                    "example.plugin",
                    "procedural.render",
                    99,
                    futurePayload,
                    DateTimeOffset.UtcNow),
            ],
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        JsonArray provenance = json[nameof(Element.Provenance)]!.AsArray();
        JsonObject first = provenance[0]!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ContainsKey("producerId"), Is.True);
            Assert.That(first.ContainsKey("schemaVersion"), Is.True);
            Assert.That(first.ContainsKey("operation"), Is.True);
            Assert.That(first.ContainsKey("generatedAt"), Is.True);
            Assert.That(restored.Provenance, Has.Length.EqualTo(2));
            Assert.That(restored.Provenance[0].GeneratedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(restored.Provenance[1].SchemaVersion, Is.EqualTo(99));
            Assert.That(
                JsonNode.DeepEquals(
                    JsonNode.Parse(restored.Provenance[1].Payload.GetRawText()),
                    JsonNode.Parse(futurePayload.GetRawText())),
                Is.True);
        }
    }

    [Test]
    public void OrdinaryElement_DoesNotSerializeEmptyProvenance()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Element());

        Assert.That(json.ContainsKey(nameof(Element.Provenance)), Is.False);
    }

    [Test]
    public void MalformedEntry_IsDroppedWithoutBlockingElementLoadOrValidEntries()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Element { Name = "Keep element" });
        json[nameof(Element.Provenance)] = new JsonArray
        {
            new JsonObject
            {
                ["producerId"] = "invalid producer name",
                ["schemaVersion"] = 1,
                ["operation"] = "image.generate",
                ["payload"] = new JsonObject(),
                ["generatedAt"] = "2026-08-09T00:00:00Z",
            },
            new JsonObject
            {
                ["producerId"] = "example.plugin",
                ["schemaVersion"] = 12,
                ["operation"] = "image.generate",
                ["payload"] = new JsonObject { ["unknown"] = true },
                ["generatedAt"] = "2026-08-09T00:00:00Z",
            },
        };

        Element? restored = null;
        Assert.DoesNotThrow(() =>
            restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored!.Name, Is.EqualTo("Keep element"));
            Assert.That(restored.Provenance, Has.Length.EqualTo(1));
            Assert.That(restored.Provenance[0].SchemaVersion, Is.EqualTo(12));
            Assert.That(restored.Provenance[0].Payload.GetProperty("unknown").GetBoolean(), Is.True);
        }
    }

    [TestCase("invalid producer name", "image.generate")]
    [TestCase("beutl.ai", "image generate with spaces")]
    public void Constructor_RejectsInvalidNames(string producerId, string operation)
    {
        Assert.Throws<ArgumentException>(() => new GenerationProvenance(
            producerId,
            operation,
            1,
            JsonSerializer.SerializeToElement(new { }),
            DateTimeOffset.UtcNow));
    }

    [Test]
    public void TryCreate_NeverPublishesInvalidInstance()
    {
        bool created = GenerationProvenance.TryCreate(
            "invalid producer",
            "image.generate",
            1,
            JsonSerializer.SerializeToElement(new { }),
            DateTimeOffset.UtcNow,
            out GenerationProvenance? provenance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.False);
            Assert.That(provenance, Is.Null);
        }
    }

    [Test]
    public void EqualityAndHashCode_UseStructuralJsonPayloadSemantics()
    {
        DateTimeOffset generatedAt = DateTimeOffset.Parse("2026-08-09T03:00:00Z");
        JsonElement firstPayload = JsonDocument.Parse(
            """{"outer":{"a":1,"b":[true,"value"]},"count":1.0}""").RootElement.Clone();
        JsonElement reorderedPayload = JsonDocument.Parse(
            """{"count":1,"outer":{"b":[true,"value"],"a":1.00}}""").RootElement.Clone();
        var first = new GenerationProvenance(
            "beutl.ai",
            "image.generate",
            1,
            firstPayload,
            generatedAt);
        var reordered = new GenerationProvenance(
            "beutl.ai",
            "image.generate",
            1,
            reorderedPayload,
            generatedAt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(reordered));
            Assert.That(first.GetHashCode(), Is.EqualTo(reordered.GetHashCode()));
            Assert.That(new HashSet<GenerationProvenance> { first, reordered }, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Updates_ApplyPreserveAppendReplaceAndClearExplicitly()
    {
        GenerationProvenance first = Create("beutl.ai", "image.generate", 1, new { });
        GenerationProvenance second = Create("example.plugin", "image.filter", 1, new { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                GenerationProvenanceUpdate.Preserve.ApplyTo([first]),
                Is.EqualTo(new[] { first }));
            Assert.That(
                GenerationProvenanceUpdate.Append([second]).ApplyTo([first]),
                Is.EqualTo(new[] { first, second }));
            Assert.That(
                GenerationProvenanceUpdate.Replace([second]).ApplyTo([first]),
                Is.EqualTo(new[] { second }));
            Assert.That(
                GenerationProvenanceUpdate.Clear.ApplyTo([first]),
                Is.Empty);
        }
    }

    [Test]
    public void ElementProperty_RejectsNullWithoutRevalidatingValidInstances()
    {
        var element = new Element();

        Assert.Throws<ArgumentException>(() =>
            element.SetValue(Element.ProvenanceProperty, new GenerationProvenance[] { null! }.ToImmutableArray()));
        Assert.That(element.Provenance, Is.Empty);
    }

    [Test]
    public void CapacityPolicy_IsSharedByUpdatesAndElementPropertyWithoutTruncation()
    {
        GenerationProvenance[] records = Enumerable.Range(
                0,
                GenerationProvenanceCollection.Capacity + 1)
            .Select(index => Create("beutl.ai", "image.generate", 1, new { index }))
            .ToArray();
        var element = new Element();

        GenerationProvenanceCapacityException replaceError = Assert.Throws<GenerationProvenanceCapacityException>(
            () => GenerationProvenanceUpdate.Replace(records))!;
        GenerationProvenanceCapacityException appendError = Assert.Throws<GenerationProvenanceCapacityException>(
            () => GenerationProvenanceUpdate.Append([records[^1]])
                .ApplyTo(records[..^1].ToImmutableArray()))!;
        GenerationProvenanceCapacityException propertyError = Assert.Throws<GenerationProvenanceCapacityException>(
            () => element.Provenance = records.ToImmutableArray())!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(replaceError.ActualCount, Is.EqualTo(records.Length));
            Assert.That(appendError.Capacity, Is.EqualTo(GenerationProvenanceCollection.Capacity));
            Assert.That(propertyError.ActualCount, Is.EqualTo(records.Length));
            Assert.That(element.Provenance, Is.Empty);
        }
    }

    [Test]
    public void Deserialization_TruncatesOverCapacityOptionalProvenanceWithoutBlockingElementLoad()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Element());
        var records = new JsonArray();
        for (int index = 0; index <= GenerationProvenanceCollection.Capacity; index++)
        {
            records.Add(JsonSerializer.SerializeToNode(
                Create("beutl.ai", "image.generate", 1, new { index })));
        }
        json[nameof(Element.Provenance)] = records;

        Element? restored = null;
        Assert.DoesNotThrow(() =>
            restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element)));

        Assert.That(restored!.Provenance, Has.Length.EqualTo(GenerationProvenanceCollection.Capacity));
        Assert.That(
            restored.Provenance.Select(item => item.Payload.GetProperty("index").GetInt32()),
            Is.EqualTo(Enumerable.Range(0, GenerationProvenanceCollection.Capacity)));
    }

    private static GenerationProvenance Create(
        string producer,
        string operation,
        int schemaVersion,
        object payload)
        => new(
            producer,
            operation,
            schemaVersion,
            JsonSerializer.SerializeToElement(payload),
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(9)));
}
