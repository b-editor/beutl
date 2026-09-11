using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DataContextEventsTests
{
    [AvaloniaTest]
    public void Disposing_stops_future_data_context_callbacks()
    {
        var control = new TextBlock();
        var attached = new List<object>();
        var detached = new List<object>();
        using IDisposable subscription = control.SubscribeDataContextChange<object>(attached.Add, detached.Add);

        subscription.Dispose();
        control.DataContext = new object();

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.Empty);
            Assert.That(detached, Is.Empty);
        });
    }

    [AvaloniaTest]
    public void Disposing_releases_the_current_context_once()
    {
        var context = new object();
        var control = new TextBlock { DataContext = context };
        var attached = new List<object>();
        var detached = new List<object>();
        using IDisposable subscription = control.SubscribeDataContextChange<object>(attached.Add, detached.Add);

        subscription.Dispose();
        subscription.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(new[] { context }));
            Assert.That(detached, Is.EqualTo(new[] { context }));
        });
    }

    [AvaloniaTest]
    public void Disposing_during_a_data_context_event_does_not_reactivate_the_subscription()
    {
        var control = new TextBlock();
        int attached = 0;
        IDisposable? subscription = null;
        control.DataContextChanged += (_, _) => subscription?.Dispose();
        using (subscription = control.SubscribeDataContextChange<object>(_ => attached++, _ => { }))
        {
            control.DataContext = new object();
            Assert.That(attached, Is.Zero);
        }
    }

    [AvaloniaTest]
    public void Reattaching_a_view_balances_its_context_callbacks()
    {
        var context = new object();
        var control = new TextBlock { DataContext = context };
        var attached = new List<object>();
        var detached = new List<object>();
        using IDisposable subscription = control.SubscribeDataContextChange<object>(attached.Add, detached.Add);
        var window = new Window { Content = control };
        try
        {
            window.Show();
            HeadlessTestHelpers.Settle();
            window.Content = null;
            window.Content = control;
            HeadlessTestHelpers.Settle();
            subscription.Dispose();
            window.Content = null;

            Assert.Multiple(() =>
            {
                Assert.That(attached, Is.EqualTo(new[] { context, context }));
                Assert.That(detached, Is.EqualTo(new[] { context, context }));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
