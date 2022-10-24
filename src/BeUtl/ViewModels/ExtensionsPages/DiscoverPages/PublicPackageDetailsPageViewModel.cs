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
                try
                {
                    await package.RefreshAsync();
                    int totalCount = 0;
                    int prevCount = 0;

                    do
                    {
                        Release[] array = await package.GetReleasesAsync(totalCount, 30);
                        if (Array.Find(array, x => x.IsPublic.Value) is { } publicRelease)
                        {
                            await publicRelease.RefreshAsync();
                            LatestRelease.Value = publicRelease;
                            break;
                        }

                        totalCount += array.Length;
                        prevCount = array.Length;
                    } while (prevCount == 30);
                }
                catch (Exception e)
                {
                    ErrorHandle(e);
                }
            });

        Refresh.Execute();
    }

    public Package Package { get; }

    public ReactivePropertySlim<Release> LatestRelease { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; }

    public override void Dispose()
    {

    }
}
