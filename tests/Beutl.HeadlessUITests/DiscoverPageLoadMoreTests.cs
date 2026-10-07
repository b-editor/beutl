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
// and the featured list could not be paged past the first page.
[TestFixture]
public class DiscoverPageLoadMoreTests
{
    [AvaloniaTest]
    public async Task Show_more_tile_loads_the_next_featured_page()
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

            Button? showMore = view.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.DataContext is LoadMoreItem);
            Assert.That(
                showMore,
                Is.Not.Null,
                "the Show more tile did not materialize headlessly - revisit the test host before trusting it");

            showMore!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => handler.FeaturedRequests.Count == 2 && !viewModel.IsBusy.Value);

            Assert.That(
                handler.FeaturedRequests.Last().Query,
                Does.Contain("start=2"),
                "Show more must request the page after the items already shown");
            Assert.That(
                viewModel.Items.OfType<LoadMoreItem>(),
                Is.Empty,
                "the consumed Show more tile must be removed before the next page is appended");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && uri.AbsolutePath.EndsWith("/api/v3/discover/featured", StringComparison.Ordinal))
            {
                FeaturedRequests.Enqueue(uri);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
