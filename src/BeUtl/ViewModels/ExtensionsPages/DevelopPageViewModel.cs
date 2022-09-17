using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DevelopPages;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public sealed class DevelopPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private IReadOnlyReactiveProperty<AuthorizedUser?> _user;

    public DevelopPageViewModel(BeutlClients clients)
    {
        _user = clients.AuthorizedUser;
        _user.Skip(1).Subscribe(async _ =>
        {
            if (Refresh.CanExecute())
            {
                await Refresh.ExecuteAsync();
            }
        });

        Refresh.Subscribe(async () =>
        {
            if (_user.Value == null)
            {
                Packages.Clear();
                return;
            }

            try
            {
                IsBusy.Value = true;
                await _user.Value!.RefreshAsync();
                Packages.Clear();

                int prevCount = 0;
                int count = 0;

                do
                {
                    Package[] items = await _user.Value!.Profile.GetPackagesAsync(count, 30);
                    Packages.AddRange(items.AsSpan());
                    prevCount = items.Length;
                    count += items.Length;
                } while (prevCount == 30);
            }
            catch
            {
                // Todo
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        _user.Where(x => x != null)
            .Timeout(TimeSpan.FromSeconds(10))
            .Subscribe(
                _ => Refresh.Execute(),
                ex => { },
                () => { });
    }

    public CoreList<Package> Packages { get; } = new();

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public CreatePackageDialogViewModel CreatePackageDialog()
    {
        return new CreatePackageDialogViewModel(_user.Value!);
    }

    public PackageDetailsPageViewModel CreatePackageDetailPage(Package package)
    {
        return new PackageDetailsPageViewModel(_user.Value!, package);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Packages.Clear();

        GC.SuppressFinalize(this);
    }
}
