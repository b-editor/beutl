using System.Net.Http;
using System.Reactive.Subjects;

using Beutl.Api.Objects;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageReleaseRefreshCoordinatorTests
{
    private readonly StoreClients _store = new();

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await _store.DisposeAsync();

    [TestCase(true)]
    [TestCase(false)]
    public async Task RefreshAsync_DoesNotPublishPartialPagesAndRestoresOnlyPreviousReadinessOnFailure(bool wasReady)
    {
        using var releasesReady = new BehaviorSubject<bool>(wasReady);
        Release previousRelease = CreateRelease("0.9.0");
        Release[] publishedReleases = [previousRelease];
        var requestedOffsets = new List<int>();
        var readyStates = new List<bool>();
        using IDisposable subscription = releasesReady.Subscribe(readyStates.Add);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PackageReleaseRefreshCoordinator.RefreshAsync(
            releasesReady,
            () => Task.CompletedTask,
            (start, _) =>
            {
                requestedOffsets.Add(start);
                return start == 0
                    ? Task.FromResult(Enumerable.Range(1, 30).Select(x => CreateRelease($"1.0.{x}")).ToArray())
                    : Task.FromException<Release[]>(new InvalidOperationException("second page failed"));
            },
            releases => publishedReleases = releases));

        Assert.Multiple(() =>
        {
            Assert.That(requestedOffsets, Is.EqualTo(new[] { 0, 30 }));
            Assert.That(publishedReleases, Is.EqualTo(new[] { previousRelease }));
            Assert.That(readyStates, Is.EqualTo(wasReady ? new[] { true, false, true } : new[] { false, false }));
            Assert.That(releasesReady.Value, Is.EqualTo(wasReady));
        });
    }

    [Test]
    public async Task RefreshAsync_PublishesCompleteSnapshotBeforeSignalingReady()
    {
        using var releasesReady = new BehaviorSubject<bool>(false);
        Release[] firstPage = Enumerable.Range(1, 30).Select(x => CreateRelease($"1.0.{x}")).ToArray();
        Release finalRelease = CreateRelease("2.0.0");
        Release[]? publishedReleases = null;
        bool? readyWhilePublishing = null;
        var readyStates = new List<bool>();
        using IDisposable subscription = releasesReady.Subscribe(readyStates.Add);

        await PackageReleaseRefreshCoordinator.RefreshAsync(
            releasesReady,
            () => Task.CompletedTask,
            (start, _) => Task.FromResult(start == 0 ? firstPage : new[] { finalRelease }),
            releases =>
            {
                publishedReleases = releases;
                readyWhilePublishing = releasesReady.Value;
            });

        Assert.Multiple(() =>
        {
            Assert.That(publishedReleases, Is.EqualTo(firstPage.Append(finalRelease)));
            Assert.That(readyWhilePublishing, Is.False);
            Assert.That(readyStates, Is.EqualTo(new[] { false, false, true }));
            Assert.That(releasesReady.Value, Is.True);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RefreshAsync_RestoresOnlyPreviousReadinessWhenPublicationFails(bool wasReady)
    {
        using var releasesReady = new BehaviorSubject<bool>(wasReady);
        var readyStates = new List<bool>();
        using IDisposable subscription = releasesReady.Subscribe(readyStates.Add);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PackageReleaseRefreshCoordinator.RefreshAsync(
            releasesReady,
            () => Task.CompletedTask,
            (_, _) => Task.FromResult(Array.Empty<Release>()),
            _ => throw new InvalidOperationException("publication failed")));

        Assert.Multiple(() =>
        {
            Assert.That(readyStates, Is.EqualTo(wasReady ? new[] { true, false, true } : new[] { false, false }));
            Assert.That(releasesReady.Value, Is.EqualTo(wasReady));
        });
    }

    [Test]
    public async Task RefreshAsync_LoadsThePackageAndReleasesTogetherAndPublishesAfterBoth()
    {
        using var releasesReady = new BehaviorSubject<bool>(false);
        var package = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = new TaskCompletionSource<Release[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Release[]? publishedReleases = null;

        Task refresh = PackageReleaseRefreshCoordinator.RefreshAsync(
            releasesReady,
            () => package.Task,
            (_, _) => releases.Task,
            published => publishedReleases = published);

        Release release = CreateRelease("1.0.0");
        releases.SetResult([release]);
        await Task.Delay(50);
        Assert.That(publishedReleases, Is.Null, "releases must wait for the package");

        package.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(publishedReleases, Is.EqualTo(new[] { release }));
        Assert.That(releasesReady.Value, Is.True);
    }

    [Test]
    public async Task RefreshAsync_FailsWithoutPublishingWhenThePackageFailsWhileReleasesLoad()
    {
        using var releasesReady = new BehaviorSubject<bool>(true);
        var releases = new TaskCompletionSource<Release[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool releasesRequested = false;
        bool published = false;

        Task refresh = PackageReleaseRefreshCoordinator.RefreshAsync(
            releasesReady,
            () => Task.FromException(new InvalidOperationException("package failed")),
            (_, _) =>
            {
                releasesRequested = true;
                return releases.Task;
            },
            _ => published = true);

        Assert.That(releasesRequested, Is.True, "both requests must start together");
        Assert.That(refresh.IsCompleted, Is.False, "the refresh must wait for the releases request");

        releases.SetResult([]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(published, Is.False);
        Assert.That(releasesReady.Value, Is.True);
    }

    private Release CreateRelease(string version) => _store.CreateRelease(version);
}
