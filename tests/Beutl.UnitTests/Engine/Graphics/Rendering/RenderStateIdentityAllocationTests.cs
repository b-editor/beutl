using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public sealed class RenderStateIdentityAllocationTests
{
    [Test]
    public void ValueStateValidationDoesNotBoxAfterShapeInitialization()
    {
        var state = new Rect(1, 2, 3, 4);
        for (int index = 0; index < 32; index++)
            RenderIdentityKeyValidator.ThrowIfInvalidState(state, "state");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
            RenderIdentityKeyValidator.ThrowIfInvalidState(state, "state");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero,
            "A validated value state must not enter the object-based terminal walk or box during recording.");
    }
}
