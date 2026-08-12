using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Covers the derivation every CWT-synthesized cache-key helper in the renderer routes through.
/// </summary>
/// <remarks>
/// <see cref="EngineObject.Resource.GetOriginal"/> returns null for a resource that never went through
/// <see cref="EngineObject.ToResource"/> — a shape the public
/// <c>FilterEffectContext.RegisterBrush</c>/<c>RegisterPen</c> entry points accept.
/// </remarks>
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
