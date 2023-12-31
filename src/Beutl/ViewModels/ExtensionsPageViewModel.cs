using Beutl.Api;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ExtensionsPageViewModel : IPageContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _authDisposables = [];
    private readonly BeutlApiApplication _clients;
    private Lazy<DiscoverPageViewModel>? _discover;
    private Lazy<LibraryPageViewModel>? _library;
    private Lazy<DevelopPageViewModel>? _develop;

    public ExtensionsPageViewModel(BeutlApiApplication clients)
    {
        _clients = clients;
        IsAuthorized = clients.AuthorizedUser
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _clients.AuthorizedUser.Subscribe(user =>
            {
                if (user == null)
                {
                    _authDisposables.Clear();
                    _discover = null;
                    _library = null;
                    _develop = null;
                }
                else
                {
                    _discover = new(() => new DiscoverPageViewModel(_clients)
                        .DisposeWith(_authDisposables));
                    _library = new(() => new LibraryPageViewModel(user, _clients)
                        .DisposeWith(_authDisposables));
                    _develop = new(() => new DevelopPageViewModel(user, _clients)
                        .DisposeWith(_authDisposables));
                }
            })
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAuthorized { get; }

    public DiscoverPageViewModel Discover
        => _discover?.Value ?? throw new Exception("Authorization is required.");

    public LibraryPageViewModel Library
        => _library?.Value ?? throw new Exception("Authorization is required.");

    public DevelopPageViewModel Develop
        => _develop?.Value ?? throw new Exception("Authorization is required.");

    public PageExtension Extension => ExtensionsPageExtension.Instance;

    public string Header => Strings.Extensions;

    public void Dispose()
    {
        _disposables.Dispose();
        _authDisposables.Dispose();
    }
}
