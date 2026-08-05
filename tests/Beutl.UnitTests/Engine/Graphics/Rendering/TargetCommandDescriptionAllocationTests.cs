using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class TargetCommandDescriptionAllocationTests
{
    private const int Iterations = 20000;
    private static readonly object s_explicitKey = new();

    [Test]
    public void DefaultStructuralKey_AllocatesNoMoreThanAnExplicitOne()
    {
        Warm();

        long withDefaultKey = MeasureBytesPerCall(structuralKey: null);
        long withExplicitKey = MeasureBytesPerCall(s_explicitKey);

        TestContext.Out.WriteLine($"default key: {withDefaultKey} bytes/call");
        TestContext.Out.WriteLine($"explicit key: {withExplicitKey} bytes/call");
        Assert.That(
            withDefaultKey,
            Is.LessThanOrEqualTo(withExplicitKey),
            "resolving the default structural key runs once per node per frame and must not allocate");
    }

    private static void Warm()
    {
        for (int index = 0; index < 200; index++)
        {
            _ = Create(null);
            _ = Create(s_explicitKey);
        }
    }

    private static long MeasureBytesPerCall(object? structuralKey)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = Create(structuralKey);
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Iterations;
    }

    private static TargetCommandDescription Create(object? structuralKey)
        => TargetCommandDescription.Create(
            Execute,
            TargetRegion.Full,
            Rect.Empty,
            RenderHitTestContract.None,
            structuralKey: structuralKey);

    private static void Execute(TargetCommandSession session)
    {
    }
}
