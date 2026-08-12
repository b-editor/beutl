using System.Reactive.Linq;
using System.Reactive.Subjects;

using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Helpers;

[TestFixture]
public class EngineObjectHelperTests
{
    // The subscription creates and updates its resource inside a posted render-thread callback, and the
    // render thread installs no unhandled-exception handler, so an escaping exception unwinds its loop
    // and every later render on that thread is lost.
    [Test]
    public void A_failing_resource_factory_reports_through_the_observer_and_spares_the_render_thread()
    {
        var probe = new ProbeObject();
        var time = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        var failure = new InvalidOperationException("the resource factory rejected the current state");
        Exception? reported = null;
        using var reportedSignal = new ManualResetEventSlim();

        using (probe
                   .SubscribeEngineVersionedResource<ProbeObject, EngineObject.Resource>(
                       time,
                       (_, _) => throw failure)
                   .Subscribe(
                       _ => { },
                       ex =>
                       {
                           reported = ex;
                           reportedSignal.Set();
                       }))
        {
            Assert.That(reportedSignal.Wait(TimeSpan.FromSeconds(30)), Is.True,
                "the failure never reached the observer");
        }

        Assert.That(reported, Is.SameAs(failure));

        using var stillAlive = new ManualResetEventSlim();
        RenderThread.Dispatcher.Dispatch(stillAlive.Set);
        Assert.That(stillAlive.Wait(TimeSpan.FromSeconds(30)), Is.True,
            "the render thread stopped taking work after the failed callback");
    }

    [SuppressResourceClassGeneration]
    private sealed class ProbeObject : EngineObject;
}
