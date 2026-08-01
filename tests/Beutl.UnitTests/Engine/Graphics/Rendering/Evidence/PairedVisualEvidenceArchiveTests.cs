using System.Security.Cryptography;
using System.Text.Json;

using Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

[TestFixture]
public sealed class PairedVisualEvidenceArchiveTests
{
    [Test]
    public void HistoricalInputsMatchTheRecordedResultAndEveryBlobHash()
    {
        string evidence = GpuPassFusionEvidencePaths.Discover().EvidenceDirectory;
        using JsonDocument result = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(evidence, "paired-visual-result.json")));

        VerifyLane(
            Path.Combine(evidence, "paired-visual-run", "target"),
            result.RootElement.GetProperty("targetManifestSha256").GetString());
        VerifyLane(
            Path.Combine(evidence, "paired-visual-run", "feature"),
            result.RootElement.GetProperty("featureManifestSha256").GetString());
    }

    private static void VerifyLane(string laneDirectory, string? expectedManifestSha256)
    {
        string manifestPath = Path.Combine(laneDirectory, "manifest.json");
        Assert.That(Sha256(manifestPath), Is.EqualTo(expectedManifestSha256));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement artifacts = manifest.RootElement.GetProperty("artifactSha256");
        Assert.That(artifacts.EnumerateObject().Count(), Is.EqualTo(44));
        foreach (JsonProperty artifact in artifacts.EnumerateObject())
        {
            string path = Path.Combine(laneDirectory, artifact.Name);
            Assert.That(File.Exists(path), Is.True, $"Archived paired-visual input is missing: {path}");
            Assert.That(Sha256(path), Is.EqualTo(artifact.Value.GetString()), artifact.Name);
        }
    }

    private static string Sha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
