using System.Text.Json;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiProvenanceFactoryTests
{
    private static readonly DateTimeOffset s_generatedAt =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(9));

    private static IEnumerable<TestCaseData> FactoryCases()
    {
        yield return Case(
            "image generation",
            AiProvenanceFactory.ImageGeneration("1536x1024", s_generatedAt),
            "image.generate",
            ["size"],
            []);
        yield return Case(
            "image edit",
            AiProvenanceFactory.ImageEdit(
                "outpaint",
                "local-source-element",
                25,
                s_generatedAt),
            "image.edit.outpaint",
            ["task", "expansionPercent"],
            ["sourceImage"]);
        yield return Case(
            "video generation",
            AiProvenanceFactory.VideoGeneration(
                6,
                "1080p",
                true,
                true,
                "local-first-element",
                "local-last-element",
                s_generatedAt),
            "video.generate",
            ["durationSeconds", "resolution", "hasFirstFrame", "hasLastFrame"],
            ["firstFrame", "lastFrame"]);
        yield return Case(
            "transcription",
            AiProvenanceFactory.Transcription(
                "scene_mix",
                TimeSpan.FromSeconds(61),
                "ja",
                2,
                s_generatedAt),
            "audio.transcribe",
            ["source", "durationSeconds", "language", "chunkCount"],
            []);
        yield return Case(
            "translation",
            AiProvenanceFactory.Translation("ja", "en", 3, s_generatedAt),
            "subtitle.translate",
            ["sourceLanguage", "targetLanguage", "batchCount"],
            []);
        yield return Case(
            "history image",
            AiProvenanceFactory.ImportedHistoryResult(
                "image.generate",
                "1024x1024",
                null,
                null,
                null,
                s_generatedAt),
            "image.generate",
            ["size"],
            []);
        yield return Case(
            "history image edit",
            AiProvenanceFactory.ImportedHistoryResult(
                "image.edit.remove.background",
                null,
                null,
                null,
                "remove_background",
                s_generatedAt),
            "image.edit.remove.background",
            ["task"],
            []);
        yield return Case(
            "history video",
            AiProvenanceFactory.ImportedHistoryResult(
                "video.generate",
                null,
                8,
                "720p",
                null,
                s_generatedAt),
            "video.generate",
            ["durationSeconds", "resolution"],
            []);
    }

    [TestCaseSource(nameof(FactoryCases))]
    public void BuiltInFactory_PersistsOnlyWhitelistedLocalMetadata(
        GenerationProvenance provenance,
        string operation,
        string[] parameterKeys,
        string[] sourceElementKeys)
    {
        JsonElement payload = provenance.Payload;
        string[] topLevelKeys = payload.EnumerateObject().Select(property => property.Name).ToArray();
        string[] actualParameterKeys = payload.GetProperty("parameters")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        string[] actualSourceKeys = payload.GetProperty("sourceElements")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        var element = new Element { Provenance = [provenance] };
        string persisted = CoreSerializer.SerializeToJsonObject(element).ToJsonString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provenance.ProducerId, Is.EqualTo("beutl.ai"));
            Assert.That(provenance.Operation, Is.EqualTo(operation));
            Assert.That(provenance.SchemaVersion, Is.EqualTo(1));
            Assert.That(provenance.GeneratedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(topLevelKeys, Is.EquivalentTo(new[] { "parameters", "sourceElements" }));
            Assert.That(actualParameterKeys, Is.EquivalentTo(parameterKeys));
            Assert.That(actualSourceKeys, Is.EquivalentTo(sourceElementKeys));
            Assert.That(persisted, Does.Not.Contain("prompt"));
            Assert.That(persisted, Does.Not.Contain("jobId"));
            Assert.That(persisted, Does.Not.Contain("fileId"));
            Assert.That(persisted, Does.Not.Contain("resultUrl"));
            Assert.That(persisted, Does.Not.Contain("accountId"));
            Assert.That(persisted, Does.Not.Contain("userId"));
        }
    }

    private static TestCaseData Case(
        string name,
        GenerationProvenance provenance,
        string operation,
        string[] parameterKeys,
        string[] sourceElementKeys)
        => new TestCaseData(provenance, operation, parameterKeys, sourceElementKeys)
            .SetName($"BuiltInFactory_{name.Replace(' ', '_')}");
}
