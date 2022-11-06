using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;

namespace BeUtl.ViewModels.ExtensionsPages.DiscoverPages;
public class DataContextFactory
{
    private readonly DiscoverService _discoverService;
    private readonly BeutlApiApplication _application;

    public DataContextFactory(DiscoverService discoverService, BeutlApiApplication application)
    {
        _discoverService = discoverService;
        _application = application;
    }

    public RankingPageViewModel RankingPage(RankingType rankingType = RankingType.Overall)
    {
        return new RankingPageViewModel(_discoverService, rankingType);
    }

    public SearchPageViewModel SearchPage(string keyword)
    {
        return new SearchPageViewModel(_discoverService, keyword);
    }

    public PublicPackageDetailsPageViewModel PublicPackageDetailPage(Package package)
    {
        return new PublicPackageDetailsPageViewModel(package, _application);
    }
}
