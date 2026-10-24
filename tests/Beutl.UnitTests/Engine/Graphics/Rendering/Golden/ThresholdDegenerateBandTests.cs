using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
[NonParallelizable]
public sealed class ThresholdDegenerateBandTests
{
    private const int Size = 100;

    private static readonly Rect s_content = new(0, 0, Size, Size);

    // Inside the ellipse's bounding box but outside the ellipse, so the stage is handed a transparent pixel.
    private static readonly Point s_corner = new(2, 2);

    private static readonly float[] s_values = [0f, 25f, 50f, 100f];
    private static readonly float[] s_smoothnesses = [0f, 1f, 50f, 100f];

    [TestCaseSource(nameof(ParameterGrid))]
    public void EveryParameterPair_PaintsFinitePixels(float value, float smoothness, float strength)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = FilteredShapeHarness.Render(s_content, Effect(value, smoothness, strength));

            Assert.That(
                ImageMetrics.FirstNonFinite(("threshold", rendered)),
                Is.Null,
                $"Threshold Value = {value}, Smoothness = {smoothness}, Strength = {strength} "
                + "emitted a non-finite component.");
        });
    }

    [TestCaseSource(nameof(ParameterGrid))]
    public void TheHitTest_ForwardsToTheUnfilteredShape(float value, float smoothness, float strength)
    {
        Assert.That(
            FilteredShapeHarness.HitTest(s_content, Effect(value, smoothness, strength), s_corner),
            Is.False);
    }

    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(50f)]
    [TestCase(100f)]
    public void AZeroThreshold_LeavesATransparentPixelHalfOpaqueAtEverySmoothness(float smoothness)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = FilteredShapeHarness.Render(s_content, Effect(0f, smoothness, 100f));

            Assert.That(
                ImageMetrics.AlphaAt(rendered, (int)s_corner.X, (int)s_corner.Y),
                Is.EqualTo(0.5f).Within(0.003f),
                $"Threshold Value = 0, Smoothness = {smoothness} moved the value the stage carries at its "
                + "own threshold.");
        });
    }

    private static IEnumerable<TestCaseData> ParameterGrid()
    {
        foreach (float value in s_values)
        {
            foreach (float smoothness in s_smoothnesses)
                yield return new TestCaseData(value, smoothness, 100f);
        }

        // Strength scales the result away from the input luma, so it decides whether a transparent pixel is
        // painted at all; the collapsed band has to stay finite and honest across it too.
        yield return new TestCaseData(0f, 0f, 0f);
        yield return new TestCaseData(0f, 0f, 50f);
    }

    private static Threshold Effect(float value, float smoothness, float strength)
        => new()
        {
            Value = { Value = value },
            Smoothness = { Value = smoothness },
            Strength = { Value = strength },
        };
}
