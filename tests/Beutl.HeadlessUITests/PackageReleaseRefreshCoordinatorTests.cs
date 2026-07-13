using System.Net.Http;
using System.Reactive.Subjects;

using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageReleaseRefreshCoordinatorTests
{
    private readonly HttpClient _httpClient;
    private readonly BeutlApiApplication _clients;

    public PackageReleaseRefreshCoordinatorTests()
    {
        _httpClient = new HttpClient();
        _clients = new BeutlApiApplication(_httpClient, new ExtensionProvider());
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _httpClient.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RefreshAsync_DoesNotPublishPartialPagesAndRestoresOnlyPreviousReadinessOnFailure(bool wasReady)
    {
        using var releasesReady = new BehaviorSubject<bool>(wasReady);
        Release previousRelease = CreateRelease("0.9.0");
        Release[] publishedReleases = [previousRelease];
        var requestedOffsets = new List<int>();
        var readyStates = new List<bool>();
        using IDisposable subscription = releasesReady.Subscribe(readyStates.Add);

        Assert.ThrowsAsync<InvalidOperationException>(() => PackageReleaseRefreshCoordinator.RefreshAsync(
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
    public void RefreshAsync_RestoresOnlyPreviousReadinessWhenPublicationFails(bool wasReady)
    {
        using var releasesReady = new BehaviorSubject<bool>(wasReady);
        var readyStates = new List<bool>();
        using IDisposable subscription = releasesReady.Subscribe(readyStates.Add);

        Assert.ThrowsAsync<InvalidOperationException>(() => PackageReleaseRefreshCoordinator.RefreshAsync(
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

    private Release CreateRelease(string version)
    {
        var ownerResponse = new ProfileResponse
        {
            Id = "owner",
            Name = "owner",
            DisplayName = "Owner",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var owner = new Profile(ownerResponse, _clients);
        var package = new Package(owner, new PackageResponse
        {
            Id = "package",
            Owner = ownerResponse,
            Name = "Package",
            DisplayName = "Package",
            Description = "",
            ShortDescription = "",
            WebSite = "",
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = null,
            Price = null,
            Paid = false,
            Owned = true,
        }, _clients);

        return new Release(package, new ReleaseResponse
        {
            Id = version,
            Version = version,
            Title = version,
            Description = "",
            TargetVersion = null,
            FileId = null,
            FileUrl = null,
        }, _clients);
    }
}
