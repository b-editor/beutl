using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using FluentAvalonia.UI.Media;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DispatcherTimerOwnershipTests
{
    [AvaloniaTest]
    public async Task Color_dropper_created_on_a_worker_processes_cancellation_on_the_UI_dispatcher()
    {
        var completion = new TaskCompletionSource<(Color2 Color, int X, int Y)>();
        using ColorDropper dropper = await Task.Run(() =>
            new ColorDropper(completion, new CancellationToken(canceled: true)));

        // Cancellation is handled on the first timer tick, before any platform-specific sampling.
        // A timer attached to the worker's unpumped Dispatcher never completes this operation.
        dropper.Start();
        Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.SameAs(completion.Task), "The timer must tick on the running UI dispatcher.");
            Assert.That(completion.Task.IsCanceled, Is.True);
        });
    }
}
