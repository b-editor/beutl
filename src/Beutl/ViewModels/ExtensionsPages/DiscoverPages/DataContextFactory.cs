using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Services;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

public class DataContextFactory(DiscoverService discoverService, BeutlApiApplication application, EditorService editorService, ProjectService projectService)
{
    public SearchPageViewModel SearchPage(string keyword)
    {
        return new SearchPageViewModel(discoverService, keyword);
    }

    public PackageDetailsPageViewModel PackageDetailPage(Package package)
    {
        return new PackageDetailsPageViewModel(package, application, editorService, projectService);
    }
}
