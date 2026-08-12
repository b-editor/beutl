using System.Net;
using System.Net.Http.Json;

using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;

namespace Beutl.UnitTests.PackageTools;

[TestFixture]
public sealed class ChangesModelTests
{
    [Test]
    public async Task Load_ClassifiesActionsAndDeduplicatesIdsWithinEachAction()
    {
        using var handler = new DelegateHandler((request, _) =>
            Task.FromResult(CreatePackageResponse(request)));
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var model = new ChangesModel();

        await model.Load(
            app,
            ["shared-package/1.0.0", "install-package/1.0.0", "install-package/2.0.0"],
            ["shared-package/3.0.0", "uninstall-package/1.0.0", "uninstall-package/2.0.0"],
            ["shared-package/2.0.0", "update-package/1.0.0", "update-package/2.0.0"],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                model.InstallItems.Select(item => (item.Id, item.Version.ToString(), item.Action)),
                Is.EqualTo(new[]
                {
                    ("shared-package", "1.0.0", PackageChangeAction.Install),
                    ("install-package", "1.0.0", PackageChangeAction.Install),
                }));
            Assert.That(
                model.UpdateItems.Select(item => (item.Id, item.Version.ToString(), item.Action)),
                Is.EqualTo(new[]
                {
                    ("shared-package", "2.0.0", PackageChangeAction.Update),
                    ("update-package", "1.0.0", PackageChangeAction.Update),
                }));
            Assert.That(
                model.UninstallItems.Select(item => (item.Id, item.Version.ToString(), item.Action)),
                Is.EqualTo(new[]
                {
                    ("shared-package", "3.0.0", PackageChangeAction.Uninstall),
                    ("uninstall-package", "1.0.0", PackageChangeAction.Uninstall),
                }));
            Assert.That(
                model.InstallItems.Concat(model.UpdateItems).Concat(model.UninstallItems),
                Is.All.Matches<PackageChangeModel>(item => item.IsRemote));
        }
    }

    [Test]
    public async Task Load_CancellationAfterRequestPropagatesWithoutAddingAnItem()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;
        using var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            Interlocked.Increment(ref requestCount);
            requestStarted.TrySetResult();
            await releaseResponse.Task.WaitAsync(cancellationToken);
            return CreatePackageResponse(request);
        });
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellation = new CancellationTokenSource();
        var model = new ChangesModel();

        Task load = model.Load(
            app,
            ["cancel-package/1.0.0"],
            ["not-reached-uninstall/1.0.0"],
            ["not-reached-update/1.0.0"],
            cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await load.WaitAsync(TimeSpan.FromSeconds(5)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestCount, Is.EqualTo(1));
            Assert.That(model.InstallItems, Is.Empty);
            Assert.That(model.UpdateItems, Is.Empty);
            Assert.That(model.UninstallItems, Is.Empty);
        }
    }

    [Test]
    public async Task Load_CancellationAfterFirstItemLeavesCollectionsEmpty()
    {
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestCount = 0;
        using var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            int count = Interlocked.Increment(ref requestCount);
            if (count > 1)
            {
                secondRequestStarted.TrySetResult();
                await releaseResponse.Task.WaitAsync(cancellationToken);
            }

            return CreatePackageResponse(request);
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellation = new CancellationTokenSource();
        var model = new ChangesModel();

        Task load = model.Load(
            app,
            ["first-package/1.0.0", "second-package/1.0.0"],
            [],
            [],
            cancellation.Token);
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await load.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(model.InstallItems, Is.Empty, "partially parsed items must not be published");
    }

    [Test]
    public async Task Load_CancellationAfterParsingLeavesCollectionsEmpty()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            HttpResponseMessage response = CreatePackageResponse(request);
            // Cancel while the response is being processed, before the publish loops run.
            cancellation.Cancel();
            requestCompleted.TrySetResult();
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var model = new ChangesModel();

        Task load = model.Load(
            app,
            ["parsed-package/1.0.0"],
            [],
            [],
            cancellation.Token);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await load.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(model.InstallItems, Is.Empty, "items must not be published after cancellation");
    }

    private static HttpResponseMessage CreatePackageResponse(HttpRequestMessage request)
    {
        string name = request.RequestUri!.Segments[^1];
        var response = new PackageResponse
        {
            Id = $"id-{name}",
            Owner = new ProfileResponse
            {
                Id = "owner-id",
                Name = "owner",
                DisplayName = "Owner",
                Bio = null,
                IconId = null,
                IconUrl = null,
            },
            Name = name,
            DisplayName = $"Display {name}",
            Description = $"Description {name}",
            ShortDescription = $"Short description {name}",
            WebSite = "https://example.com",
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = null,
            Price = null,
            Paid = false,
            Owned = false,
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(response),
        };
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
