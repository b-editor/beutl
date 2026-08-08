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

    [Test]
    public void GetCacheKey_OnADetachedResource_ReturnsASynthesizedIdentityInsteadOfThrowing()
    {
        using var detached = new EngineObject.Resource();

        object first = DeferredOpaqueSource.GetCacheKey(detached);
        object second = DeferredOpaqueSource.GetCacheKey(detached);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second),
                "the synthesized identity is held weakly against the resource, so it survives between reads");
            Assert.That(first, Is.Not.EqualTo(Guid.Empty),
                "a detached resource must not collapse onto the default backing id");
        });
    }

    [Test]
    public void GetCacheKey_OnTwoDetachedResources_KeepsThemApart()
    {
        using var first = new EngineObject.Resource();
        using var second = new EngineObject.Resource();

        Assert.That(
            DeferredOpaqueSource.GetCacheKey(first),
            Is.Not.EqualTo(DeferredOpaqueSource.GetCacheKey(second)));
    }

    [Test]
    public void GetCacheKey_OnAnAttachedResource_EqualsTheBackingObjectId()
    {
        Brush.Resource attached = Brushes.Resource.White;

        Assert.Multiple(() =>
        {
            Assert.That(DeferredOpaqueSource.GetCacheKey(attached), Is.EqualTo(attached.RequireOriginal().Id),
                "an attached resource keeps the cache identity it had before the helper was routed");
            Assert.That(
                DeferredOpaqueSource.GetCacheKey(attached).GetHashCode(),
                Is.EqualTo(attached.RequireOriginal().Id.GetHashCode()),
                "every consumer of this key buckets by hash before comparing");
        });
    }

    [Test]
    public void GetCacheKey_OnAnAttachedResource_AllocatesNoMoreThanReadingTheIdDirectly()
    {
        Brush.Resource attached = Brushes.Resource.White;

        long routed = MeasureBytesPerCall(() => DeferredOpaqueSource.GetCacheKey(attached));
        long direct = MeasureBytesPerCall(() => attached.RequireOriginal().Id);

        TestContext.Out.WriteLine($"routed: {routed} bytes/call, direct: {direct} bytes/call");
        Assert.That(routed, Is.EqualTo(direct),
            "both forms box exactly one Guid, so routing the helper moves no allocation into Process");
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
