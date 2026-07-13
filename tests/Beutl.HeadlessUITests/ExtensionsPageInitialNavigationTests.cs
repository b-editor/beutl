using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using NUnit.Framework;

namespace Beutl.HeadlessUITests;

// Guards that the extensions dialog shows the first tab's content immediately on open, without the
// user having to click a tab. Regression: the initial frame.Navigate ran in OnAttachedToVisualTree,
// before the child Frame's template was applied, so the first page never rendered until a tab switch
// retried navigation from a ready Frame.
[TestFixture]
public class ExtensionsPageInitialNavigationTests
{
    [AvaloniaTest]
    public async Task Opens_with_first_tab_rendered_without_tab_switch()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;

        var clients = new BeutlApiApplication(
            new HttpClient(new EmptyJsonArrayHandler()),
            new ExtensionProvider());
        var vm = new ExtensionsPageViewModel(
            clients,
            mainViewModel.EditorService,
            mainViewModel.ProjectService);

        var page = new ExtensionsPage { DataContext = vm };
        try
        {
            page.Show();
            HeadlessTestHelpers.Render();

            bool discoverRendered = page.GetVisualDescendants().OfType<DiscoverPage>().Any();
            Assert.That(
                discoverRendered,
                Is.True,
                "the first tab (Discover) must render on open without a manual tab switch");
        }
        finally
        {
            page.Close();
            vm.Dispose();
            HeadlessTestHelpers.Settle();
        }
    }

    private sealed class EmptyJsonArrayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
