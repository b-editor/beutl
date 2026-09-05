using Beutl.Editor.Components.Helpers;
using Beutl.Engine;

namespace Beutl.E2ETests;

// Beutl.Editor.Components grants InternalsVisibleTo only to Beutl and Beutl.UnitTests, so this project is
// the one place a reader outside that friendship can be written. Every call below therefore compiles only
// while the handle's reading surface stays public - an editor plugin reaching a published resource has no
// other way in, and the internal gate behind the handle must not drag any of it out of reach.
[TestFixture]
public class EngineResourceHandleVisibilityTests
{
    // A handle over no resource is what a subscriber holds before the first publication and after the
    // release, so answering every entry point without a gate is part of the public contract, not a detail.
    [Test]
    public void A_handle_over_no_resource_answers_every_reader()
    {
        EngineResourceHandle<ProbeResource> handle = default;

        Assert.Multiple(() =>
        {
            Assert.That(handle.Version, Is.Zero);
            Assert.That(
                handle.Read(_ => Assert.Fail("an empty handle lent out a resource")), Is.False);
            Assert.That(handle.Read(_ => "read the resource", "no resource"), Is.EqualTo("no resource"));
            Assert.That(
                handle.Project<ChildResource>(_ =>
                {
                    Assert.Fail("an empty handle lent out a resource");
                    return null;
                }),
                Is.Null);
        });
    }

    // Handles are compared to tell a republication from a redelivery of the same one, which is what a
    // subscriber outside this assembly needs to decide whether its view has to be rebuilt.
    [Test]
    public void Handles_over_the_same_nothing_compare_equal()
    {
        EngineResourceHandle<ProbeResource> handle = default;
        EngineResourceHandle<ProbeResource> same = default;

        Assert.Multiple(() =>
        {
            Assert.That(handle.Equals(same), Is.True);
            Assert.That(handle.Equals((object)same), Is.True);
            Assert.That(handle == same, Is.True);
            Assert.That(handle != same, Is.False);
            Assert.That(handle.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        });
    }

    // Only ever a type argument: the constraint on the handle has to be satisfiable from out here too.
    private sealed class ProbeResource : EngineObject.Resource;

    private sealed class ChildResource : EngineObject.Resource;
}
