using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// Regression for #3764: the extensions window's top navigation selects the item of the page its frame
// shows, whether the frame got there forward, back, or to a page under Home.
[TestFixture]
public class ExtensionsPageNavigationSelectionTests
{
    [AvaloniaTest]
    public async Task Selection_follows_the_page_the_frame_shows()
    {
        await TestReset.ResetShellAsync();
        MainViewModel main = TestShell.MainViewModel;
        using var httpClient = new HttpClient(new EmptyJsonArrayHandler());
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var viewModel = new ExtensionsPageViewModel(clients, main.EditorService, main.ProjectService);
        var page = new ExtensionsPage { DataContext = viewModel };

        try
        {
            page.Show();
            HeadlessTestHelpers.Render();
            FANavigationView nav = page.FindControl<FANavigationView>("nav")!;
            FAFrame frame = page.FindControl<FAFrame>("frame")!;
            FANavigationViewItem[] items = nav.MenuItemsSource!.Cast<FANavigationViewItem>().ToArray();
            FANavigationViewItem home = items.Single(item => item.Tag as Type == typeof(DiscoverPage));
            FANavigationViewItem library = items.Single(item => item.Tag as Type == typeof(LibraryPage));
            Assert.That(nav.SelectedItem, Is.SameAs(home));

            frame.Navigate(typeof(LibraryPage), viewModel.Library);
            HeadlessTestHelpers.Render();
            Assert.That(nav.SelectedItem, Is.SameAs(library), "showing the library must select Library");

            frame.Navigate(typeof(SearchPage), "sample");
            HeadlessTestHelpers.Render();
            Assert.That(nav.SelectedItem, Is.SameAs(home), "a page under Home must select Home");

            frame.GoBack();
            HeadlessTestHelpers.Render();
            Assert.That(frame.Content, Is.InstanceOf<LibraryPage>());
            Assert.That(nav.SelectedItem, Is.SameAs(library), "going back to the library must select Library");

            frame.GoBack();
            HeadlessTestHelpers.Render();
            Assert.That(frame.Content, Is.InstanceOf<DiscoverPage>());
            Assert.That(nav.SelectedItem, Is.SameAs(home), "going back to Home must select Home");
        }
        finally
        {
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private sealed class EmptyJsonArrayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }
}
