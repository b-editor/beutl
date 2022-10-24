using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DiscoverPages;

public sealed class PublicPackageDetailsPageViewModel : BasePageViewModel
{
    public PublicPackageDetailsPageViewModel(Package package)
    {
        Package = package;
        Refresh = new AsyncReactiveCommand()
            .WithSubscribe(async () =>
            {

            });
    }

    public Package Package { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {

    }
}
