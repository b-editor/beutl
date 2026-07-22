using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

[TestFixture]
public sealed class GpuPassFusionNonVacuityTests
{
    private static readonly Lazy<GpuPassFusionEvidenceManifest> s_manifest =
        new(GpuPassFusionBaselineEvidence.LoadAndVerify);

    private static IEnumerable<TestCaseData> ParityWorkloads
    {
        get
        {
            foreach (GpuPassFusionEvidenceScene scene in s_manifest.Value.Scenes.Where(
                         item => item.Role == "parity"))
            {
                yield return new TestCaseData(scene.Id)
                    .SetName($"RecordedNonVacuity_{scene.Id.Replace('-', '_')}");
            }
        }
    }

    [TestCaseSource(nameof(ParityWorkloads))]
    public void RecordedNonVacuity_IsRecomputedFromReferenceAndControl(string sceneId)
    {
        GpuPassFusionEvidenceScene scene = s_manifest.Value.GetScene(sceneId);
        GpuPassFusionNonVacuityRecord recorded = scene.NonVacuity
            ?? throw new InvalidDataException($"Parity scene '{scene.Id}' has no recorded non-vacuity evidence.");
        GpuPassFusionNonVacuityDelta actual =
            GpuPassFusionBaselineEvidence.RecomputeNonVacuity(s_manifest.Value, scene);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.LinearRgbMae, Is.EqualTo(recorded.LinearRgbMae).Within(1e-12));
            Assert.That(actual.AlphaMae, Is.EqualTo(recorded.AlphaMae).Within(1e-12));
            Assert.That(
                actual.MaximumChannelError,
                Is.EqualTo(recorded.MaximumChannelError).Within(1e-12));
            Assert.That(actual.SampleCount, Is.EqualTo(recorded.SampleCount));
            Assert.That(
                recorded.ParityTolerance,
                Is.EqualTo(GpuPassFusionBaselineEvidence.NonVacuityParityTolerance));
            Assert.That(
                actual.MarginAboveTolerance,
                Is.EqualTo(recorded.MarginAboveTolerance).Within(1e-12));
            Assert.That(
                actual.MarginAboveTolerance,
                Is.GreaterThan(0),
                $"Workload '{scene.Id}' does not distinguish its reference from control beyond parity tolerance.");
        }
    }

    [TestCase("primary-control-gamma-disabled")]
    [TestCase("primary-control-opacity-disabled")]
    [TestCase("primary-control-invert-disabled")]
    public void PrimaryChain_EachDisabledStageIsNonVacuous(string controlSceneId)
    {
        GpuPassFusionEvidenceManifest manifest = s_manifest.Value;
        GpuPassFusionEvidenceScene referenceScene = manifest.GetScene("primary-cross-node");
        GpuPassFusionEvidenceScene controlScene = manifest.GetScene(controlSceneId);
        using Bitmap reference = ReadScene(manifest, referenceScene);
        using Bitmap control = ReadScene(manifest, controlScene);

        GpuPassFusionNonVacuityDelta delta = GpuPassFusionBaselineEvidence.CalculateNonVacuityDelta(
            reference,
            control,
            "full-frame",
            metricRegion: null);

        Assert.That(
            Math.Max(delta.LinearRgbMae, delta.AlphaMae),
            Is.GreaterThan(0),
            $"Disabling stage control '{controlSceneId}' produced no measurable output delta.");
    }

    private static Bitmap ReadScene(
        GpuPassFusionEvidenceManifest manifest,
        GpuPassFusionEvidenceScene scene)
    {
        string blob = scene.Blob
            ?? throw new InvalidDataException($"Scene '{scene.Id}' has no immutable RGBA16F blob.");
        return Rgba16fGoldenStore.Read(
            Path.Combine(manifest.Paths.BaselineDirectory, blob),
            scene.BlobWidth,
            scene.BlobHeight);
    }
}
