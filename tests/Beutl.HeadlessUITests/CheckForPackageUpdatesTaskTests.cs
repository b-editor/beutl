using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// At startup, packages that failed the API check share one short warning when they have an update.
// The others keep their full error.
[TestFixture, NonParallelizable]
public sealed class CheckForPackageUpdatesTaskTests
{
    private StoreClients _clients = null!;
    private NotificationCapture _notifications = null!;

    [SetUp]
    public void SetUp()
    {
        _clients = new StoreClients();
        _notifications = new NotificationCapture();
    }

    [TearDown]
    public async Task TearDown()
    {
        _notifications.Dispose();
        await _clients.DisposeAsync();
    }

    [Test]
    public async Task Failed_packages_with_updates_share_one_warning()
    {
        await Run([Mismatch("A"), Mismatch("B")], [Update("A"), Update("B")]);

        Notification warning = _notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(warning.Title, Is.EqualTo(MessageStrings.PackageUpdatesRequired));
            Assert.That(warning.Message, Is.EqualTo(string.Format(MessageStrings.PackagesNotLoadedOnThisVersion, 2)));
            Assert.That(warning.Actions!.Single().Text, Is.EqualTo(Strings.Open));
        }
    }

    [Test]
    public async Task A_failed_package_without_an_update_keeps_its_error()
    {
        await Run([Mismatch("A"), Mismatch("B")], [Update("A")]);

        Notification[] shown = [.. _notifications.All];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown, Has.Length.EqualTo(2));
            Assert.That(shown[0].Type, Is.EqualTo(NotificationType.Error));
            Assert.That(shown[0].Title, Is.EqualTo(string.Format(ExtensionsStrings.PackageNotLoaded, "B")));
            Assert.That(shown[1].Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(shown[1].Message, Is.EqualTo(string.Format(MessageStrings.PackagesNotLoadedOnThisVersion, 1)));
        }
    }

    [Test]
    public async Task A_failed_update_check_keeps_the_errors()
    {
        await Run([Mismatch("A")], _ => throw new HttpRequestException("The store is unreachable."));

        AssertOnlyTheErrorOf("A");
    }

    // Waiting for the extensions also waits for the side-loaded ones, which can fail on their own.
    [Test]
    public async Task A_failed_wait_for_the_extensions_keeps_the_errors()
    {
        await Run(
            [Mismatch("A")],
            _ => Task.FromResult<IReadOnlyList<PackageUpdate>>([]),
            extensionsLoaded: Task.FromException(new InvalidOperationException("boom")));

        AssertOnlyTheErrorOf("A");
    }

    [Test]
    public async Task A_store_that_does_not_answer_does_not_hold_back_the_errors()
    {
        var check = new TaskCompletionSource<IReadOnlyList<PackageUpdate>>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task task = Run([Mismatch("A")], _ => check.Task, mismatchDeadline: TimeSpan.FromMilliseconds(1));
        await HeadlessTestHelpers.WaitUntilAsync(() => !_notifications.All.IsEmpty, pump: false);

        AssertOnlyTheErrorOf("A");

        check.SetResult([Update("A")]);
        await task;

        Notification[] shown = [.. _notifications.All];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown, Has.Length.EqualTo(2));
            Assert.That(shown[1].Type, Is.EqualTo(NotificationType.Information));
            Assert.That(shown[1].Message, Is.EqualTo(string.Format(MessageStrings.PackagesCanBeUpdated, 1)));
        }
    }

    [Test]
    public async Task Shutting_down_during_the_check_shows_nothing()
    {
        await Run([Mismatch("A")], _ => throw new OperationCanceledException(), shuttingDown: true);

        Assert.That(_notifications.All, Is.Empty);
    }

    [Test]
    public async Task Updates_of_other_packages_are_counted_apart_from_the_warning()
    {
        await Run([Mismatch("A")], [Update("A"), Update("B"), Update("C")]);

        Notification[] shown = [.. _notifications.All];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shown, Has.Length.EqualTo(2));
            Assert.That(shown[0].Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(shown[0].Message, Is.EqualTo(string.Format(MessageStrings.PackagesNotLoadedOnThisVersion, 1)));
            Assert.That(shown[1].Type, Is.EqualTo(NotificationType.Information));
            Assert.That(shown[1].Message, Is.EqualTo(string.Format(MessageStrings.PackagesCanBeUpdated, 2)));
        }
    }

    // AfterLoadingExtensionsTask reports the failures that are not API mismatches.
    [Test]
    public async Task Other_load_failures_do_not_change_the_notice_about_updates()
    {
        await Run([(new LocalPackage { Name = "A" }, new InvalidOperationException("boom"))], [Update("A")]);

        Notification notice = _notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notice.Type, Is.EqualTo(NotificationType.Information));
            Assert.That(notice.Message, Is.EqualTo(string.Format(MessageStrings.PackagesCanBeUpdated, 1)));
        }
    }

    [Test]
    public async Task An_update_found_in_the_store_matches_the_failed_package_whatever_its_case()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string name = "Outdated." + Guid.NewGuid().ToString("N");
        using var registration = new AppRootFileBackup("installedPackages.json");
        using var store = new StoreHandler(TestStore.CreatePackageResponse(name.ToUpperInvariant()), TestStore.CreateReleaseResponse("2.0.0"));
        using var http = new HttpClient(store, disposeHandler: false);
        await using var app = new BeutlApiApplication(http, new ExtensionProvider());
        app.GetResource<InstalledPackageRepository>()
            .UpgradePackagesAndDeferNotifications(new PackageIdentity(name, NuGetVersion.Parse("1.0.0")))();

        await Run([Mismatch(name)], app.GetResource<PackageManager>().CheckUpdate);

        Assert.That(_notifications.Single().Type, Is.EqualTo(NotificationType.Warning));
    }

    private void AssertOnlyTheErrorOf(string name)
    {
        Notification error = _notifications.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Type, Is.EqualTo(NotificationType.Error));
            Assert.That(error.Title, Is.EqualTo(string.Format(ExtensionsStrings.PackageNotLoaded, name)));
        }
    }

    private static (LocalPackage, Exception) Mismatch(string name)
    {
        var package = new LocalPackage { Name = name, Version = "1.0.0" };
        return (package, new PackageApiMismatchException(
            package, [new(PackageApiProblemKind.MissingType, "Beutl.Engine", "Beutl.Engine.Gone")]));
    }

    private PackageUpdate Update(string name)
    {
        Release release = TestStore.CreateRelease(_clients.App, "2.0.0", name: name);
        return new PackageUpdate(release.Package, null, release);
    }

    private static Task Run((LocalPackage, Exception)[] failures, PackageUpdate[] updates)
        => Run(failures, _ => Task.FromResult<IReadOnlyList<PackageUpdate>>(updates));

    private static Task Run(
        (LocalPackage, Exception)[] failures,
        Func<CancellationToken, Task<IReadOnlyList<PackageUpdate>>> checkUpdate,
        bool shuttingDown = false,
        Task? extensionsLoaded = null,
        TimeSpan? mismatchDeadline = null)
    {
        return new CheckForPackageUpdatesTask(
                () => extensionsLoaded ?? Task.CompletedTask,
                () => failures,
                checkUpdate,
                () => shuttingDown,
                mismatchDeadline ?? TimeSpan.FromMinutes(1))
            .Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
