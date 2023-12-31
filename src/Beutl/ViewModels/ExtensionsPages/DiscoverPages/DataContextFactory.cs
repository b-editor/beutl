using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;
public class DataContextFactory(DiscoverService discoverService, BeutlApiApplication application)
{
    public RankingPageViewModel RankingPage(RankingType rankingType = RankingType.Overall)
    {
        return new RankingPageViewModel(discoverService, rankingType);
    }

    public SearchPageViewModel SearchPage(string keyword)
    {
        return new SearchPageViewModel(discoverService, keyword);
    }

    public PublicPackageDetailsPageViewModel PublicPackageDetailPage(Package package)
    {
        return new PublicPackageDetailsPageViewModel(package, application);
    }
}
