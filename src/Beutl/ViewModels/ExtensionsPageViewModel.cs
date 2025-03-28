using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ExtensionsPageViewModel : IPageContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _authDisposables = [];
    private Lazy<DiscoverPageViewModel>? _discover;
    private Lazy<LibraryPageViewModel>? _library;

    public ExtensionsPageViewModel(BeutlApiApplication clients)
    {
        IsAuthorized = clients.AuthorizedUser
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        clients.AuthorizedUser.Subscribe(user =>
            {
                _authDisposables.Clear();
                _discover = new(() => new DiscoverPageViewModel(clients)
                    .DisposeWith(_authDisposables));

                _library = new(() => new LibraryPageViewModel(user, clients)
                    .DisposeWith(_authDisposables));
            })
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAuthorized { get; }

    public DiscoverPageViewModel Discover => _discover!.Value;

    public LibraryPageViewModel Library => _library!.Value;

    public PageExtension Extension => ExtensionsPageExtension.Instance;

    public string Header => Strings.Extensions;

    public void Dispose()
    {
        _disposables.Dispose();
        _authDisposables.Dispose();
    }
}
