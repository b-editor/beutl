using Beutl.Api;

using BeUtl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class ExtensionsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly CompositeDisposable _authDisposables = new();
    private readonly BeutlClients _clients;
    private HomePageViewModel? _home;
    private LibraryPageViewModel? _library;
    private DevelopPageViewModel? _develop;

    public ExtensionsPageViewModel(BeutlClients clients)
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
                    _home = null;
                    _library = null;
                    _develop = null;
                }
                else
                {
                    _home = new HomePageViewModel(_clients)
                        .DisposeWith(_authDisposables);
                    _library = new LibraryPageViewModel(user)
                        .DisposeWith(_authDisposables);
                    _develop = new DevelopPageViewModel(user)
                        .DisposeWith(_authDisposables);
                }
            })
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAuthorized { get; }

    public HomePageViewModel Home
        => _home ?? throw new Exception("Authorization is required.");
    
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
