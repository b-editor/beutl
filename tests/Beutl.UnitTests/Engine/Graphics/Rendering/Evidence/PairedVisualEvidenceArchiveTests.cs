using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

[TestFixture]
public sealed class PairedVisualEvidenceArchiveTests
{
    private const string ExpectedResultSha256 =
        "84013ea15e4b98c57af91ac994ea8098fc883c0ff53fd933ce71da6c49014ec5";

    private static readonly string[] SemanticRefreshSceneIds =
    [
        "scene3d-with-2d-tail",
    ];

    [Test]
    public void HistoricalResultAndInputsMatchThePinnedRunAndEveryBlobHash()
    {
        GpuPassFusionEvidenceStackSliceGate.RequireStack4EvidenceSlice();
        string evidence = GpuPassFusionEvidencePaths.Discover().EvidenceDirectory;
        string resultPath = Path.Combine(evidence, "paired-visual-result.json");
        string archivedResultPath = Path.Combine(evidence, "paired-visual-run", "paired-result.json");
        byte[] resultBytes = File.ReadAllBytes(resultPath);
        byte[] archivedResultBytes = File.ReadAllBytes(archivedResultPath);
        Assert.Multiple(() =>
        {
            Assert.That(Sha256(resultBytes), Is.EqualTo(ExpectedResultSha256));
            Assert.That(archivedResultBytes, Is.EqualTo(resultBytes));
        });
        using JsonDocument result = JsonDocument.Parse(resultBytes);

        VerifyLane(
            Path.Combine(evidence, "paired-visual-run", "target"),
            result.RootElement.GetProperty("targetManifestSha256").GetString());
        VerifyLane(
            Path.Combine(evidence, "paired-visual-run", "feature"),
            result.RootElement.GetProperty("featureManifestSha256").GetString());
        VerifySemanticRefreshNonVacuity(evidence, result.RootElement);
    }

    private static void VerifySemanticRefreshNonVacuity(string evidence, JsonElement result)
    {
        using JsonDocument committed = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(evidence, "target-baseline", "manifest.json")));
        using JsonDocument archived = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(evidence, "paired-visual-run", "target", "manifest.json")));
        JsonElement[] refreshes = result.GetProperty("semanticRefresh").GetProperty("artifacts")
            .EnumerateArray()
            .ToArray();
        string[] actualSceneIds = refreshes
            .Select(artifact => artifact.GetProperty("sceneId").GetString()
                ?? throw new InvalidDataException("A semantic-refresh result has no scene id."))
            .ToArray();
        Assert.That(actualSceneIds, Is.EqualTo(SemanticRefreshSceneIds));

        foreach (string sceneId in SemanticRefreshSceneIds)
        {
            JsonElement committedRecord = FindScene(committed.RootElement, sceneId).GetProperty("nonVacuity");
            JsonElement archivedRecord = FindScene(archived.RootElement, sceneId).GetProperty("nonVacuity");
            Assert.That(
                JsonNode.DeepEquals(JsonNode.Parse(archivedRecord.GetRawText()), JsonNode.Parse(committedRecord.GetRawText())),
                Is.True,
                $"Archived non-vacuity metrics do not describe the refreshed '{sceneId}' blob.");
        }
    }

    private static JsonElement FindScene(JsonElement manifest, string sceneId)
        => manifest.GetProperty("scenes").EnumerateArray().Single(
            scene => string.Equals(scene.GetProperty("id").GetString(), sceneId, StringComparison.Ordinal));

    private static void VerifyLane(string laneDirectory, string? expectedManifestSha256)
    {
        string manifestPath = Path.Combine(laneDirectory, "manifest.json");
        Assert.That(Sha256(manifestPath), Is.EqualTo(expectedManifestSha256));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement artifacts = manifest.RootElement.GetProperty("artifactSha256");
        Assert.That(artifacts.EnumerateObject().Count(), Is.EqualTo(44));
        string[] expectedFiles = artifacts.EnumerateObject()
            .Select(artifact => artifact.Name)
            .Append("manifest.json")
            .Order()
            .ToArray();
        string[] actualFiles = Directory.GetFiles(laneDirectory)
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidDataException($"Archived paired-visual input has no file name: {path}"))
            .Order()
            .ToArray();
        Assert.That(actualFiles, Is.EqualTo(expectedFiles), $"Unexpected files exist in {laneDirectory}.");
        foreach (JsonProperty artifact in artifacts.EnumerateObject())
        {
            string path = Path.Combine(laneDirectory, artifact.Name);
            Assert.That(File.Exists(path), Is.True, $"Archived paired-visual input is missing: {path}");
            Assert.That(Sha256(path), Is.EqualTo(artifact.Value.GetString()), artifact.Name);
        }
    }

    private static string Sha256(string path)
        => Sha256(File.ReadAllBytes(path));

    private static string Sha256(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
