using Avalonia.Media.Imaging;

using Beutl.Api.Objects;

using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public PackageDetailsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        Refresh.Subscribe(async () =>
        {
            if (IsBusy.Value)
                return;

            try
            {
                IsBusy.Value = true;

                await _user.RefreshAsync();

                await Package.RefreshAsync();
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        DisplayName = package.DisplayName
            .Select(x => !string.IsNullOrWhiteSpace(x) ? x : Package.Name)
            .ToReadOnlyReactivePropertySlim(Package.Name)
            .DisposeWith(_disposables);
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<string> DisplayName { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
