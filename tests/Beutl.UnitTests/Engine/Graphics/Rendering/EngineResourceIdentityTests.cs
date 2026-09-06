using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class EngineResourceIdentityTests
{
    private const int Iterations = 20000;

    [Test]
    public void ADetachedResource_HasNoBackingObjectId()
    {
        using var detached = new EngineObject.Resource();

        Assert.That(detached.GetOriginal(), Is.Null);
    }





    private static long MeasureBytesPerCall(Func<object> read)
    {
        for (int index = 0; index < 200; index++)
            _ = read();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Iterations; index++)
            _ = read();
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Iterations;
    }
}
