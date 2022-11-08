using Beutl.Api;

using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ExtensionsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly CompositeDisposable _authDisposables = new();
    private readonly BeutlApiApplication _clients;
    private DiscoverPageViewModel? _discover;
    private LibraryPageViewModel? _library;
    private DevelopPageViewModel? _develop;

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
                    _discover = new DiscoverPageViewModel(_clients)
                        .DisposeWith(_authDisposables);
                    _library = new LibraryPageViewModel(user, _clients)
                        .DisposeWith(_authDisposables);
                    _develop = new DevelopPageViewModel(user, _clients)
                        .DisposeWith(_authDisposables);
                }
            })
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAuthorized { get; }

    public DiscoverPageViewModel Discover
        => _discover ?? throw new Exception("Authorization is required.");
    
    public LibraryPageViewModel Library
        => _library ?? throw new Exception("Authorization is required.");

    public DevelopPageViewModel Develop
        => _develop ?? throw new Exception("Authorization is required.");

    public void Dispose()
    {
        _disposables.Dispose();
        _authDisposables.Dispose();
    }
}
