using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Collections;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
using AvaDispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class GradientStopPreviewLifetimeTests
{
    [AvaloniaTest]
    public void Removing_a_replacement_releases_its_preview_subscription()
    {
        var clock = new TrackingClock();
        var source = new CoreList<GradientStop>(new GradientStop(Colors.Red, 0));
        var (preview, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        using (subscription)
        {
            Assert.That(clock.SubscriberCount, Is.EqualTo(1));

            source[0] = new GradientStop(Colors.Blue, 1);
            Assert.That(clock.SubscriberCount, Is.EqualTo(1));

            source.RemoveAt(0);

            Assert.Multiple(() =>
            {
                Assert.That(preview, Is.Empty);
                Assert.That(clock.SubscriberCount, Is.Zero,
                    "Removing the replacement must release the subscription created for it.");
            });
        }
    }

    [AvaloniaTest]
    public void Disposing_after_replacement_releases_every_preview_subscription()
    {
        var clock = new TrackingClock();
        var source = new CoreList<GradientStop>(new GradientStop(Colors.Red, 0));
        var (_, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        try
        {
            source[0] = new GradientStop(Colors.Blue, 1);
        }
        finally
        {
            subscription.Dispose();
        }

        Assert.That(clock.SubscriberCount, Is.Zero);
    }

    [AvaloniaTest]
    public void Removing_one_occurrence_of_a_shared_stop_keeps_the_other_preview_live()
    {
        var clock = new TrackingClock();
        var stop = new GradientStop(Colors.Red, 0);
        var source = new CoreList<GradientStop>(stop, stop);
        var (preview, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        using (subscription)
        {
            DrainPreviewUpdates();
            var removedPreview = preview[0];
            var remainingPreview = preview[1];
            source.RemoveAt(0);

            stop.Color.CurrentValue = Colors.Blue;
            DrainPreviewUpdates();

            Assert.Multiple(() =>
            {
                Assert.That(clock.SubscriberCount, Is.EqualTo(1));
                Assert.That(preview.Single(), Is.SameAs(remainingPreview));
                Assert.That(remainingPreview.Color, Is.EqualTo(Avalonia.Media.Colors.Blue));
                Assert.That(removedPreview.Color, Is.EqualTo(Avalonia.Media.Colors.Red));
            });
        }

        Assert.That(clock.SubscriberCount, Is.Zero);
    }

    [AvaloniaTest]
    [TestCase(0, 4)]
    [TestCase(2, 0)]
    public void Moving_a_range_preserves_preview_order_and_subscription_ownership(int oldIndex, int newIndex)
    {
        var clock = new TrackingClock();
        var source = new CoreList<GradientStop>(
            new GradientStop(Colors.Red, 0), new GradientStop(Colors.Green, 0.25f),
            new GradientStop(Colors.Blue, 0.5f), new GradientStop(Colors.Yellow, 1));
        var (preview, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        using (subscription)
        {
            DrainPreviewUpdates();
            var initialPreviews = preview.ToArray();
            var initialStops = source.ToArray();

            source.MoveRange(oldIndex, 2, newIndex);

            Assert.That(preview, Is.EqualTo(new[]
            {
                initialPreviews[2], initialPreviews[3], initialPreviews[0], initialPreviews[1]
            }));

            source.RemoveAt(0);
            Assert.That(clock.SubscriberCount, Is.EqualTo(3));
            initialStops[2].Color.CurrentValue = Colors.Yellow;
            initialStops[0].Color.CurrentValue = Colors.Blue;
            DrainPreviewUpdates();
            Assert.Multiple(() =>
            {
                Assert.That(initialPreviews[2].Color, Is.EqualTo(Avalonia.Media.Colors.Blue));
                Assert.That(initialPreviews[0].Color, Is.EqualTo(Avalonia.Media.Colors.Blue));
            });
            source.Clear();
            Assert.That(clock.SubscriberCount, Is.Zero);
        }
    }

    [AvaloniaTest]
    public void Moving_one_stop_moves_its_subscription_with_its_preview()
    {
        var clock = new TrackingClock();
        var removedStop = new GradientStop(Colors.Red, 0);
        var retainedStop = new GradientStop(Colors.Blue, 1);
        var source = new CoreList<GradientStop>(removedStop, new GradientStop(Colors.Green, 0.5f), retainedStop);
        var (preview, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        using (subscription)
        {
            DrainPreviewUpdates();
            var removedPreview = preview[0];
            var retainedPreview = preview[2];

            source.Move(0, 2);
            Assert.That(preview[2], Is.SameAs(removedPreview));
            source.RemoveAt(2);
            removedStop.Color.CurrentValue = Colors.Yellow;
            retainedStop.Color.CurrentValue = Colors.Red;
            DrainPreviewUpdates();

            Assert.Multiple(() =>
            {
                Assert.That(clock.SubscriberCount, Is.EqualTo(2));
                Assert.That(preview[1], Is.SameAs(retainedPreview));
                Assert.That(retainedPreview.Color, Is.EqualTo(Avalonia.Media.Colors.Red));
                Assert.That(removedPreview.Color, Is.EqualTo(Avalonia.Media.Colors.Red));
            });
        }

        Assert.That(clock.SubscriberCount, Is.Zero);
    }

    [AvaloniaTest]
    public void Reset_rebuilds_previews_and_releases_every_old_occurrence()
    {
        var clock = new TrackingClock();
        var oldStop = new GradientStop(Colors.Red, 0);
        var source = new CoreList<GradientStop>(oldStop, oldStop) { ResetBehavior = ResetBehavior.Reset };
        var (preview, subscription) = source.ToAvaGradientStopsSync(clock.Time);
        using (subscription)
        {
            DrainPreviewUpdates();
            var oldPreviews = preview.ToArray();
            var newStop = new GradientStop(Colors.Blue, 1);

            source.Replace(new[] { newStop, newStop });
            oldStop.Color.CurrentValue = Colors.Yellow;
            DrainPreviewUpdates();

            Assert.Multiple(() =>
            {
                Assert.That(clock.SubscriberCount, Is.EqualTo(2));
                Assert.That(preview.Select(i => i.Color), Is.EqualTo(new[]
                {
                    Avalonia.Media.Colors.Blue, Avalonia.Media.Colors.Blue
                }));
                Assert.That(oldPreviews.Select(i => i.Color), Is.EqualTo(new[]
                {
                    Avalonia.Media.Colors.Red, Avalonia.Media.Colors.Red
                }));
            });
        }

        Assert.That(clock.SubscriberCount, Is.Zero);
    }

    private static void DrainPreviewUpdates()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        RenderThread.Dispatcher.Invoke(static () => { }, DispatchPriority.Low, timeout.Token);
        AvaDispatcher.UIThread.RunJobs();
    }

    private sealed class TrackingClock
    {
        public int SubscriberCount { get; private set; }

        public IObservable<TimeSpan> Time => Observable.Create<TimeSpan>(observer =>
        {
            SubscriberCount++;
            observer.OnNext(TimeSpan.Zero);
            return Disposable.Create(() => SubscriberCount--);
        });
    }
}
