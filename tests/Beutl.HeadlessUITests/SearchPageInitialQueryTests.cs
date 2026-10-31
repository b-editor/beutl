using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// Regression for #3763: the search page searches once when it opens, without a refresh or a kind switch.
[TestFixture]
public class SearchPageInitialQueryTests
{
    [AvaloniaTest]
    public async Task Opening_the_search_page_searches_once()
    {
        await TestReset.ResetShellAsync();
        MainViewModel main = TestShell.MainViewModel;
        var handler = new SearchHandler();
        using var httpClient = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = new ExtensionsPageViewModel(clients, main.EditorService, main.ProjectService);
        var window = new ExtensionsPage { DataContext = viewModel };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            FAFrame frame = window.FindControl<FAFrame>("frame")!;

            frame.Navigate(typeof(SearchPage), "sample");
            await HeadlessTestHelpers.WaitUntilAsync(
                () => frame.Content is SearchPage { DataContext: SearchPageViewModel { IsBusy.Value: false } search }
                    && search.Packages.OfType<Package>().Any(),
                TimeSpan.FromSeconds(10),
                "the search page did not show the results of its keyword");

            // Let a second search, if anything starts one, reach the handler before counting.
            await Task.Delay(100);
            HeadlessTestHelpers.Render();

            Assert.That(handler.SearchQueries, Has.Count.EqualTo(1), "opening the page must search exactly once");
            Assert.That(handler.SearchQueries.Single(), Does.Contain("query=sample"));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Answers the search with one package and records each search; the Home page's featured list is empty.
    private sealed class SearchHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

        public ConcurrentQueue<string> SearchQueries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            PackageResponse package = TestStore.CreatePackageResponse("Beutl.Sample");
            object response;
            if (uri.AbsolutePath == "/api/v3/discover/search")
            {
                SearchQueries.Enqueue(uri.Query);
                response = new[]
                {
                    new SimplePackageResponse
                    {
                        Id = package.Id,
                        Owner = package.Owner,
                        Name = package.Name,
                        DisplayName = package.DisplayName,
                        ShortDescription = package.ShortDescription,
                        Tags = package.Tags,
                        LogoId = null,
                        LogoUrl = null,
                        Currency = null,
                        Price = null,
                        Paid = false,
                        Owned = true,
                    },
                };
            }
            else if (uri.AbsolutePath == $"/api/v3/packages/{package.Name}")
            {
                response = package;
            }
            else
            {
                response = Array.Empty<SimplePackageResponse>();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response, response.GetType(), s_options), Encoding.UTF8, "application/json"),
            });
        }
    }
}
