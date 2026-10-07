using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Pages.ExtensionsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages;
using NUnit.Framework;

namespace Beutl.HeadlessUITests;

// Regression: the Discover page's "Show more" tile shares Package_Click with the package cards, but
// the handler only navigated for packages, so clicking the tile never ran DiscoverPageViewModel.More
// and the featured list could not be paged past the first page. The tile must also survive a failed
// request so the user can retry.
[TestFixture]
public class DiscoverPageLoadMoreTests
{
    [AvaloniaTest]
    public async Task Show_more_tile_loads_the_next_featured_page()
    {
        await RunWithShowMoreTileAsync(async (view, viewModel, handler) =>
        {
            ClickShowMore(view);
            await WaitFor(() => handler.FeaturedRequests.Count == 2 && !viewModel.IsBusy.Value);

            Assert.That(
                handler.FeaturedRequests.Last().Query,
                Does.Contain("start=2"),
                "Show more must request the page after the items already shown");
            Assert.That(
                viewModel.Items.OfType<LoadMoreItem>(),
                Is.Empty,
                "the consumed Show more tile must be replaced by the loaded page");
        });
    }

    [AvaloniaTest]
    public async Task Show_more_tile_stays_available_when_the_next_page_fails()
    {
        await RunWithShowMoreTileAsync(async (view, viewModel, handler) =>
        {
            handler.FailNextFeaturedRequest = true;
            ClickShowMore(view);
            await WaitFor(() => handler.FeaturedRequests.Count == 2 && !viewModel.IsBusy.Value);

            Assert.That(
                viewModel.Items.OfType<LoadMoreItem>().Count(),
                Is.EqualTo(1),
                "a failed request must leave the Show more tile in place to retry");

            HeadlessTestHelpers.Render();
            ClickShowMore(view);
            await WaitFor(() => handler.FeaturedRequests.Count == 3 && !viewModel.IsBusy.Value);

            Assert.That(
                handler.FeaturedRequests.Last().Query,
                Does.Contain("start=2"),
                "the retry must request the same page again");
            Assert.That(viewModel.Items.OfType<LoadMoreItem>(), Is.Empty);
        });
    }

    private static async Task RunWithShowMoreTileAsync(
        Func<DiscoverPage, DiscoverPageViewModel, RecordingHandler, Task> test)
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;

        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = new DiscoverPageViewModel(
            clients,
            mainViewModel.EditorService,
            mainViewModel.ProjectService);

        var view = new DiscoverPage { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            await WaitFor(() => handler.FeaturedRequests.Count == 1 && !viewModel.IsBusy.Value);

            viewModel.Items.Add(new DummyItem());
            viewModel.Items.Add(new DummyItem());
            viewModel.Items.Add(new LoadMoreItem());
            HeadlessTestHelpers.Render();

            await test(view, viewModel, handler);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void ClickShowMore(DiscoverPage view)
    {
        Button? showMore = view.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.DataContext is LoadMoreItem);
        Assert.That(
            showMore,
            Is.Not.Null,
            "the Show more tile did not materialize headlessly - revisit the test host before trusting it");

        showMore!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            HeadlessTestHelpers.Settle();
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentQueue<Uri> FeaturedRequests { get; } = new();

        public bool FailNextFeaturedRequest { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && uri.AbsolutePath.EndsWith("/api/v3/discover/featured", StringComparison.Ordinal))
            {
                FeaturedRequests.Enqueue(uri);
                if (FailNextFeaturedRequest)
                {
                    FailNextFeaturedRequest = false;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
