using System.Diagnostics;
using Beutl.Configuration;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class PackageToolsTelemetrySessionTests
{
    [Test]
    public void DesktopLaunchUsesOnlyValidatedEnvironmentSessionWhenAnalyticsIsEnabled()
    {
        string sessionId = Guid.NewGuid().ToString("N");
        ProcessStartInfo startInfo = MainViewModel.CreatePackageToolsStartInfo(
            [],
            [],
            sessionId,
            usageAnalyticsEnabled: true,
            launchDebugger: false);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.ArgumentList, Does.Not.Contain("--session-id"));
            Assert.That(string.Join(' ', startInfo.ArgumentList), Does.Not.Contain(sessionId));
            Assert.That(startInfo.Environment[Telemetry.SessionIdEnvironmentVariable], Is.EqualTo(sessionId));
        });
    }

    [Test]
    public void DesktopLaunchOmitsTelemetryEnvironmentWhenConsentIsOffOrSessionIsInvalid()
    {
        string? original = Environment.GetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                Telemetry.SessionIdEnvironmentVariable,
                Guid.NewGuid().ToString("N"));
            ProcessStartInfo consentOff = MainViewModel.CreatePackageToolsStartInfo(
                [],
                [],
                Guid.NewGuid().ToString("N"),
                usageAnalyticsEnabled: false,
                launchDebugger: false);
            ProcessStartInfo invalidSession = MainViewModel.CreatePackageToolsStartInfo(
                [],
                [],
                "not-a-session-id",
                usageAnalyticsEnabled: true,
                launchDebugger: false);

            Assert.Multiple(() =>
            {
                Assert.That(consentOff.Environment, Does.Not.ContainKey(Telemetry.SessionIdEnvironmentVariable));
                Assert.That(invalidSession.Environment, Does.Not.ContainKey(Telemetry.SessionIdEnvironmentVariable));
                Assert.That(consentOff.ArgumentList, Does.Not.Contain("--session-id"));
                Assert.That(invalidSession.ArgumentList, Does.Not.Contain("--session-id"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable, original);
        }
    }

    [Test]
    public void PackageToolsReadsOnlyAValidSessionFromItsEnvironmentWhenAnalyticsIsEnabled()
    {
        string? original = Environment.GetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable);
        string sessionId = Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable, sessionId);
            Assert.That(
                Beutl.PackageTools.UI.Program.GetTelemetrySessionId(new TelemetryConfig { UsageAnalytics = true }),
                Is.EqualTo(sessionId));
            Assert.That(
                Beutl.PackageTools.UI.Program.GetTelemetrySessionId(new TelemetryConfig { UsageAnalytics = false }),
                Is.Null);

            Environment.SetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable, "invalid-session");
            Assert.That(
                Beutl.PackageTools.UI.Program.GetTelemetrySessionId(new TelemetryConfig { UsageAnalytics = true }),
                Is.Null);

            Environment.SetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable, null);
            Assert.That(
                Beutl.PackageTools.UI.Program.GetTelemetrySessionId(new TelemetryConfig { UsageAnalytics = true }),
                Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Telemetry.SessionIdEnvironmentVariable, original);
        }
    }
}
