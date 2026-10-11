using System.Collections.Concurrent;
using System.Diagnostics;

using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;

namespace Beutl.UnitTests.Api;

// Telemetry here is the copy of src/Beutl/Services/Telemetry.cs that Beutl.PackageTools.UI compiles.
// The subscription list it builds is shared with the Beutl app.
[TestFixture]
public sealed class PackageManagementTelemetryTests
{
    [Test]
    public async Task GetPackages_IsRecorded_WhenPackageManagementTelemetryIsAllowed()
    {
        var recorded = new ConcurrentQueue<Activity>();
        using ActivityListener listener = ListenTo(
            Telemetry.GetActivitySourceNames(CreateConsent(packageManagement: true)), recorded);

        await CreatePackageManager().GetPackages();

        Assert.That(
            recorded.Where(a => a.Source == PackageManagementActivitySource.ActivitySource).Select(a => a.OperationName),
            Does.Contain("GetPackages"));
    }

    [Test]
    public async Task GetPackages_IsNotRecorded_WhenPackageManagementTelemetryIsDeclined()
    {
        var recorded = new ConcurrentQueue<Activity>();
        using ActivityListener listener = ListenTo(
            Telemetry.GetActivitySourceNames(CreateConsent(packageManagement: false)), recorded);

        await CreatePackageManager().GetPackages();

        Assert.That(recorded.Where(a => a.Source == PackageManagementActivitySource.ActivitySource), Is.Empty);
    }

    [Test]
    public async Task BeutlActivitySources_AreSubscribed_WhenAllTelemetryIsAllowed()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        // The sources these assemblies create. Beutl.Usage exists only in the Beutl app build,
        // which subscribes to it unconditionally.
        string[] sourceNames =
        [
            BeutlApplication.ActivitySource.Name,
            Telemetry.Applilcation.Name,
            PackageManagementActivitySource.ActivitySource.Name,
            app.ActivitySource.Name,
        ];

        List<string> subscribed = Telemetry.GetActivitySourceNames(CreateConsent(packageManagement: true));

        Assert.That(sourceNames.Except(subscribed), Is.Empty);
    }

    private static TelemetryConfig CreateConsent(bool packageManagement)
    {
        return new TelemetryConfig
        {
            Beutl_Application = true,
            Beutl_PackageManagement = packageManagement,
            Beutl_Api_Client = true,
            Beutl_Logging = true,
        };
    }

    // Subscribes like the tracer does: to exactly the named sources.
    private static ActivityListener ListenTo(IReadOnlyCollection<string> sourceNames, ConcurrentQueue<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static PackageManager CreatePackageManager()
    {
        return new PackageManager(
            new InstalledPackageRepository(),
            new ExtensionProvider(),
            new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry()),
            apiApplication: null!);
    }
}
