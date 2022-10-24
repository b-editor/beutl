using Avalonia.Media.Imaging;

using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageDetailsPageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AuthorizedUser _user;

    public PackageDetailsPageViewModel(AuthorizedUser user, Package package)
    {
        _user = user;
        Package = package;

        //LocalizedLogoImage = CreateResourceObservable(v => v.LogoImage)
        //    .CombineLatest(Package)
        //    .Select(t => t.First ?? t.Second)
        //    .SelectMany(async link => link != null ? await link.TryGetBitmapAsync() : null)
        //    .ToReadOnlyReactivePropertySlim()
        //    .DisposeWith(_disposables);
        LocalizedLogoImage = Observable.Return<Bitmap?>(null).ToReadOnlyReactivePropertySlim();

        HasLogoImage = LocalizedLogoImage.Select(i => i != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

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
    }

    public Package Package { get; }

    public ReadOnlyReactivePropertySlim<bool> HasLogoImage { get; }

    public ReadOnlyReactivePropertySlim<Bitmap?> LocalizedLogoImage { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    public override void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");
        _disposables.Dispose();

        GC.SuppressFinalize(this);
    }
}
