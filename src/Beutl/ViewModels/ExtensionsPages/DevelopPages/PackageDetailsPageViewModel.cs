using Beutl.Api.Objects;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel
{
    private readonly ILogger _logger = Log.ForContext<PackageDetailsPageViewModel>();
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
                using (await _user.Lock.LockAsync())
                {
                    IsBusy.Value = true;

                    await _user.RefreshAsync();

                    await Package.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                ErrorHandle(ex);
                _logger.Error(ex, "An unexpected error has occurred.");
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
