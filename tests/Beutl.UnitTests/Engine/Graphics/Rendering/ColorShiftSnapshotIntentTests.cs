using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// <see cref="ColorShift"/>'s readback failure must decide degrade-vs-fail from the explicit
/// <see cref="RenderIntent"/>, not from the working-scale ceiling that happens to accompany it.
/// </summary>
[TestFixture]
public sealed class ColorShiftSnapshotIntentTests
{
    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void DeliveryFailsRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => ColorShift.ThrowIfDeliverySnapshotFailure(context.Intent, 0),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.StartWith("ColorShift snapshot failed for target 0"));
    }

    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void PreviewDegradesRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => ColorShift.ThrowIfDeliverySnapshotFailure(context.Intent, 0),
            Throws.Nothing);
    }
}
