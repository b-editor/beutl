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
        using var notifications = NotificationCapture.Install();
        using var logs = new CapturingLoggerProvider(category => category == typeof(ExtensionGuard).FullName);
        Log.LoggerFactory.AddProvider(logs);

        string? first = ExtensionGuard.Call<string>(extension, () => throw new InvalidOperationException("broken"));
        bool second = ExtensionGuard.Call(extension, () => throw new InvalidOperationException("broken again"));
        string? third = ExtensionGuard.Call(extension, () => "works");

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
        using var notifications = NotificationCapture.Install();
        int calls = 0;

        await ExtensionGuard.CallAsync(extension, () => Task.FromException(new InvalidOperationException("command failed")));
        string? factory = ExtensionGuard.Call(extension, () => (++calls).ToString());
        await ExtensionGuard.CallAsync(extension, () => Task.FromResult(++calls));

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
        using var notifications = NotificationCapture.Install();

        ExtensionGuard.Call<string>(extension, () => null, out bool nullResult);
        ExtensionGuard.Call<string>(extension, () => throw new InvalidOperationException("broken"), out bool failure);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullResult, Is.True);
            Assert.That(failure, Is.False);
        }
    }

    [Test]
    public void A_canceled_command_is_not_a_failure()
    {
        var extension = new NamedExtension();
        ExtensionGuard.SetPackage(extension, "Canceling.Package");
        using var notifications = NotificationCapture.Install();

        Assert.CatchAsync<OperationCanceledException>(
            () => ExtensionGuard.CallAsync(extension, () => Task.FromCanceled(new CancellationToken(canceled: true))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExtensionGuard.IsTurnedOff(extension), Is.False);
            Assert.That(notifications.All, Is.Empty);
        }
    }

    private sealed class NamedExtension : Extension
    {
        public override string DisplayName => "Named";
    }
}
