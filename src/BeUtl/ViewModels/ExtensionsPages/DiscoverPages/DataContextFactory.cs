using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api.Objects;

namespace BeUtl.ViewModels.ExtensionsPages.DiscoverPages;
public class DataContextFactory
{
    private readonly DiscoverService _discoverService;

    public DataContextFactory(DiscoverService discoverService)
    {
        _discoverService = discoverService;
    }

    public RankingPageViewModel RankingPage(RankingPageViewModel.RankingType rankingType = RankingPageViewModel.RankingType.Overall)
    {
        return new RankingPageViewModel(_discoverService, rankingType);
    }
}
