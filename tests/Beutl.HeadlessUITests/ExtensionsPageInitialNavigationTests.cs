using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
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
        var vm = new ExtensionsPageViewModel(
            mainViewModel._beutlClients,
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
}
