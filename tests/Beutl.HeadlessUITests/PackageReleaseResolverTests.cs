using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;

using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PackageReleaseResolverTests
{
    [Test]
    public async Task ObserveLatest_IgnoresCompletionFromSupersededRequest()
    {
        using var requests = new Subject<PackageIdentity?>();
        var first = new TaskCompletionSource<Release>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<Release>(TaskCreationOptions.RunContinuationsAsynchronously);
        Release firstRelease = CreateRelease("1.0.0");
        Release secondRelease = CreateRelease("2.0.0");
        var observed = new List<Release?>();
        var latestObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                ImmediateScheduler.Instance,
                () => null,
                () => [],
                version => version == "1.0.0" ? first.Task : second.Task,
                ex => Assert.Fail(ex.ToString()))
            .Subscribe(release =>
            {
                observed.Add(release);
                latestObserved.TrySetResult();
            },
            ex => Assert.Fail(ex.ToString()),
            () => streamCompleted.TrySetResult());

        requests.OnNext(CreateIdentity("1.0.0"));
        requests.OnNext(CreateIdentity("2.0.0"));
        second.SetResult(secondRelease);
        await latestObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        first.SetResult(firstRelease);
        requests.OnCompleted();
        await streamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(observed, Is.EqualTo(new[] { secondRelease }));
    }

    [Test]
    public async Task ObserveLatest_EmitsNullOnFailureAndContinues()
    {
        using var requests = new Subject<PackageIdentity?>();
        Release recovered = CreateRelease("2.0.0");
        var observed = new List<Release?>();
        var errors = new List<Exception>();
        var twoResults = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                ImmediateScheduler.Instance,
                () => null,
                () => [],
                version => version == "1.0.0"
                    ? Task.FromException<Release>(new InvalidOperationException("lookup failed"))
                    : Task.FromResult(recovered),
                errors.Add)
            .Subscribe(release =>
            {
                observed.Add(release);
                if (observed.Count == 2)
                {
                    twoResults.TrySetResult();
                }
            });

        requests.OnNext(CreateIdentity("1.0.0"));
        await WaitForAsync(() => observed.Count == 1);
        requests.OnNext(CreateIdentity("2.0.0"));
        await twoResults.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(observed[0], Is.Null);
        Assert.That(observed[1], Is.SameAs(recovered));
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ObserveLatest_UsesCachedReleaseWithoutNetworkRequest()
    {
        using var requests = new Subject<PackageIdentity?>();
        Release cached = CreateRelease("1.0.0");
        var observed = new TaskCompletionSource<Release?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                ImmediateScheduler.Instance,
                () => null,
                () => [cached],
                _ =>
                {
                    requestCount++;
                    return Task.FromResult(cached);
                },
                ex => Assert.Fail(ex.ToString()))
            .Subscribe(release => observed.TrySetResult(release));

        requests.OnNext(CreateIdentity("1.0.0"));
        Release? result = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(result, Is.SameAs(cached));
        Assert.That(requestCount, Is.Zero);
    }

    [Test]
    public async Task ObserveLatest_UsesSemanticallyEquivalentCachedReleaseWithoutNetworkRequest()
    {
        using var requests = new Subject<PackageIdentity?>();
        Release cached = CreateRelease("1.0.0");
        var observed = new TaskCompletionSource<Release?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                ImmediateScheduler.Instance,
                () => null,
                () => [cached],
                _ =>
                {
                    requestCount++;
                    return Task.FromResult(cached);
                },
                ex => Assert.Fail(ex.ToString()))
            .Subscribe(release => observed.TrySetResult(release));

        requests.OnNext(CreateIdentity("1.0"));
        Release? result = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(result, Is.SameAs(cached));
        Assert.That(requestCount, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ObserveWhenReleasesReady_EmitsAfterBothInputsRegardlessOfOrder(bool readyFirst)
    {
        using var installedPackages = new Subject<PackageIdentity?>();
        using var releasesReady = new BehaviorSubject<bool>(false);
        PackageIdentity installed = CreateIdentity("1.0.0");
        var observed = new List<PackageIdentity?>();

        using IDisposable subscription = PackageReleaseResolver.ObserveWhenReleasesReady(
                installedPackages,
                releasesReady,
                ImmediateScheduler.Instance)
            .Subscribe(observed.Add);

        if (readyFirst)
        {
            releasesReady.OnNext(true);
            installedPackages.OnNext(installed);
        }
        else
        {
            installedPackages.OnNext(installed);
            Assert.That(observed, Is.Empty);
            releasesReady.OnNext(true);
        }

        Assert.That(observed, Is.EqualTo(new[] { installed }));
    }

    [Test]
    public async Task ObserveLatest_CapturesStateAndEmitsOnProvidedScheduler()
    {
        using var requests = new Subject<PackageIdentity?>();
        using var scheduler = new EventLoopScheduler();
        Release cached = CreateRelease("1.0.0");
        var schedulerThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource<Release?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int selectedReleaseThread = 0;
        int allReleasesThread = 0;
        int observerThread = 0;

        scheduler.Schedule(() => schedulerThread.TrySetResult(Environment.CurrentManagedThreadId));

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                scheduler,
                () =>
                {
                    selectedReleaseThread = Environment.CurrentManagedThreadId;
                    return null;
                },
                () =>
                {
                    allReleasesThread = Environment.CurrentManagedThreadId;
                    return [cached];
                },
                _ => Task.FromResult(cached),
                ex => Assert.Fail(ex.ToString()))
            .Subscribe(release =>
            {
                observerThread = Environment.CurrentManagedThreadId;
                observed.TrySetResult(release);
            });

        requests.OnNext(CreateIdentity("1.0.0"));
        Release? result = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int expectedThread = await schedulerThread.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(cached));
            Assert.That(selectedReleaseThread, Is.EqualTo(expectedThread));
            Assert.That(allReleasesThread, Is.EqualTo(expectedThread));
            Assert.That(observerThread, Is.EqualTo(expectedThread));
        });
    }

    [Test]
    public async Task ObserveLatest_EmitsNullWhenStateCaptureFailsAndContinues()
    {
        using var requests = new Subject<PackageIdentity?>();
        Release recovered = CreateRelease("2.0.0");
        var observed = new List<Release?>();
        var errors = new List<Exception>();
        var twoResults = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int captureCount = 0;

        using IDisposable subscription = PackageReleaseResolver.ObserveLatest(
                requests,
                ImmediateScheduler.Instance,
                () => null,
                () => ++captureCount == 1
                    ? throw new InvalidOperationException("collection changed")
                    : [recovered],
                _ => Task.FromResult(recovered),
                errors.Add)
            .Subscribe(release =>
            {
                observed.Add(release);
                if (observed.Count == 2)
                {
                    twoResults.TrySetResult();
                }
            });

        requests.OnNext(CreateIdentity("1.0.0"));
        requests.OnNext(CreateIdentity("2.0.0"));
        await twoResults.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(observed[0], Is.Null);
            Assert.That(observed[1], Is.SameAs(recovered));
            Assert.That(errors, Has.Count.EqualTo(1));
        });
    }

    private static PackageIdentity CreateIdentity(string version)
    {
        return new PackageIdentity("Package", NuGetVersion.Parse(version));
    }

    private static Release CreateRelease(string version)
    {
        var clients = new BeutlApiApplication(new HttpClient(), new ExtensionProvider());
        var ownerResponse = new ProfileResponse
        {
            Id = "owner",
            Name = "owner",
            DisplayName = "Owner",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var owner = new Profile(ownerResponse, clients);
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
        }, clients);

        return new Release(package, new ReleaseResponse
        {
            Id = version,
            Version = version,
            Title = version,
            Description = "",
            TargetVersion = null,
            FileId = null,
            FileUrl = null,
        }, clients);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
