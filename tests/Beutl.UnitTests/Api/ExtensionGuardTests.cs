using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Logging;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class ExtensionGuardTests
{
    [Test]
    public void A_failing_built_in_extension_is_reported_once_and_stays_enabled()
    {
        var extension = new NamedExtension();
        using var notifications = new NotificationCapture();
        using var logs = new CapturingLoggerProvider(category => category == typeof(ExtensionGuard).FullName);
        Log.LoggerFactory.AddProvider(logs);

        string? first = ExtensionGuard.Call<string>(extension, notifications, () => throw new InvalidOperationException("broken"));
        bool second = ExtensionGuard.Call(extension, notifications, () => throw new InvalidOperationException("broken again"));
        string? third = ExtensionGuard.Call(extension, notifications, () => "works");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.False);
            Assert.That(third, Is.EqualTo("works"));
            Assert.That(ExtensionGuard.IsTurnedOff(extension), Is.False);
            Assert.That(notifications.All.Select(n => (n.Title, n.Message)), Is.EqualTo(new[]
            {
                (string.Format(ExtensionsStrings.ExtensionFailed, "Named"), MessageStrings.OperationFailed),
            }));
            Assert.That(logs.Entries.Select(entry => entry.Exception?.Message), Is.EqualTo(new[] { "broken" }));
        }
    }

    [Test]
    public async Task A_failing_package_extension_is_turned_off_until_restart()
    {
        var extension = new NamedExtension();
        ExtensionGuard.SetPackage(extension, "Broken.Package");
        using var notifications = new NotificationCapture();
        int calls = 0;

        await ExtensionGuard.CallAsync(extension, notifications, () => Task.FromException(new InvalidOperationException("command failed")));
        string? factory = ExtensionGuard.Call(extension, notifications, () => (++calls).ToString());
        await ExtensionGuard.CallAsync(extension, notifications, () => Task.FromResult(++calls));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory, Is.Null);
            Assert.That(calls, Is.Zero);
            Assert.That(ExtensionGuard.IsTurnedOff(extension), Is.True);
            Assert.That(notifications.All.Select(n => (n.Title, n.Message)), Is.EqualTo(new[]
            {
                (string.Format(ExtensionsStrings.ExtensionFailed, "Named"), string.Format(ExtensionsStrings.ExtensionTurnedOff, "Broken.Package")),
            }));
        }
    }

    [Test]
    public void Declined_tells_a_null_result_apart_from_a_failure()
    {
        var extension = new NamedExtension();
        using var notifications = new NotificationCapture();

        ExtensionGuard.Call<string>(extension, notifications, () => null, out bool nullResult);
        ExtensionGuard.Call<string>(extension, notifications, () => throw new InvalidOperationException("broken"), out bool failure);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullResult, Is.True);
            Assert.That(failure, Is.False);
        }
    }

    [Test]
    public async Task A_canceled_command_is_not_a_failure()
    {
        var extension = new NamedExtension();
        ExtensionGuard.SetPackage(extension, "Canceling.Package");
        using var notifications = new NotificationCapture();

        await Assert.CatchAsync<OperationCanceledException>(
            () => ExtensionGuard.CallAsync(extension, notifications, () => Task.FromCanceled(new CancellationToken(canceled: true))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExtensionGuard.IsTurnedOff(extension), Is.False);
            Assert.That(notifications.All, Is.Empty);
        }
    }

    [Test]
    public void A_context_whose_service_lookup_throws_offers_no_notifications()
    {
        using var notifications = new NotificationCapture();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExtensionGuard.FindNotifications(new Services(notifications)), Is.SameAs(notifications));
            Assert.That(ExtensionGuard.FindNotifications(new Services(null)), Is.Null);
            Assert.That(ExtensionGuard.FindNotifications(new ThrowingServices()), Is.Null);
            Assert.That(ExtensionGuard.FindNotifications(new object()), Is.Null);
            Assert.That(ExtensionGuard.FindNotifications(null), Is.Null);
        }
    }

    private sealed class Services(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private sealed class ThrowingServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("broken lookup");
    }

    private sealed class NamedExtension : Extension
    {
        public override string DisplayName => "Named";
    }
}
