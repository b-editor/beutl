using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class MoreResourcesPageViewModel
{
    internal readonly PackagePageViewModel _viewModel;

    public MoreResourcesPageViewModel(PackagePageViewModel viewModel)
    {
        _viewModel = viewModel;
        ActualName = viewModel.ActualName;
    }

    public ReactivePropertySlim<string> ActualName { get; }
}
