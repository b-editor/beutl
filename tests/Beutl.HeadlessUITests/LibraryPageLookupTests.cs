using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// The library looks up the installed packages missing from it together, and shows a package the store
// does not know as a local one.
[TestFixture, NonParallelizable]
public sealed class LibraryPageLookupTests
{
    [AvaloniaTest]
    public async Task Installed_packages_are_looked_up_together_and_unknown_ones_stay_local()
    {
        await TestReset.ResetShellAsync();
        string known = "Known." + Guid.NewGuid().ToString("N");
        string unknown = "Unknown." + Guid.NewGuid().ToString("N");
        var identities = new[]
        {
            new PackageIdentity(known, NuGetVersion.Parse("1.0.0")),
            new PackageIdentity(unknown, NuGetVersion.Parse("1.0.0")),
        };
        var cleanup = new List<string>();
        using var registration = new AppRootFileBackup("installedPackages.json");
        foreach (PackageIdentity identity in identities)
        {
            TestPackages.CreateExtracted(identity, data: false, cleanup: cleanup);
        }

        var handler = new LookupHandler(known, lookups: identities.Length);
        using var http = new HttpClient(handler);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        foreach (PackageIdentity identity in identities)
        {
            app.GetResource<InstalledPackageRepository>().UpgradePackagesAndDeferNotifications(identity)();
        }
        MainViewModel main = TestShell.MainViewModel;

        try
        {
            using var library = new LibraryPageViewModel(null, app, main.EditorService, main.ProjectService);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => !library.IsBusy.Value && library.Packages.OfType<IUserPackageViewModel>().Count() == 2,
                TimeSpan.FromSeconds(20));

            Assert.Multiple(() =>
            {
                Assert.That(handler.AllStartedTogether, Is.True, "the lookups must run together");
                Assert.That(library.Packages.OfType<RemoteUserPackageViewModel>().Single().Name, Is.EqualTo(known));
                Assert.That(library.Packages.OfType<LocalUserPackageViewModel>().Single().Name, Is.EqualTo(unknown));
            });
        }
        finally
        {
            HeadlessTestHelpers.Settle();
            TestPackages.Delete(cleanup);
        }
    }

    // Holds each package lookup until all of them have started, or a timeout passes for a sequential caller.
    private sealed class LookupHandler(string known, int lookups) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        private volatile bool _timedOut;

        public bool AllStartedTogether => _allStarted.Task.IsCompleted && !_timedOut;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/v3/packages/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (Interlocked.Increment(ref _started) == lookups)
            {
                _allStarted.TrySetResult();
            }

            if (await Task.WhenAny(_allStarted.Task, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)) != _allStarted.Task)
            {
                _timedOut = true;
            }
            if (!path.EndsWith("/" + known, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            string json = JsonSerializer.Serialize(TestStore.CreatePackageResponse(known), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
