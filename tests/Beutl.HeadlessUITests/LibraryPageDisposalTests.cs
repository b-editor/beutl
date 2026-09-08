using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class LibraryPageDisposalTests
{
    [AvaloniaTest]
    public async Task Refresh_AfterDisposal_DoesNotRepopulateLocalPackages()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var editorService = new EditorService(new ExtensionProvider());
        var projectService = new ProjectService();
        var viewModel = new LibraryPageViewModel(null, app, editorService, projectService);

        // Let the constructor-triggered refresh settle before disposing.
        await Task.Delay(200);

        viewModel.Dispose();
        viewModel.Refresh.Execute();
        await Task.Delay(200);

        // The lifetime token is canceled on disposal, so a refresh admitted afterwards must
        // not repopulate the list with children that would never be disposed.
        Assert.That(viewModel.LocalPackages, Is.Empty);
    }
}
