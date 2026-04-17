using Beutl.Api;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ExtensionsPageViewModel : IToolWindowContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _authDisposables = [];
    private Lazy<DiscoverPageViewModel>? _discover;
    private Lazy<LibraryPageViewModel>? _library;

    public ExtensionsPageViewModel(BeutlApiApplication clients)
    {
        IsAuthenticated = clients.AuthenticatedUser
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        clients.AuthenticatedUser.Subscribe(user =>
            {
                _authDisposables.Clear();
                _discover = new(() => new DiscoverPageViewModel(clients)
                    .DisposeWith(_authDisposables));

                _library = new(() => new LibraryPageViewModel(user, clients)
                    .DisposeWith(_authDisposables));
            })
            .DisposeWith(_disposables);
    }

    public ReadOnlyReactivePropertySlim<bool> IsAuthenticated { get; }

    public DiscoverPageViewModel Discover => _discover!.Value;

    public LibraryPageViewModel Library => _library!.Value;

    public ToolWindowExtension Extension => ExtensionsToolWindowExtension.Instance;

    public string Header => Strings.Extensions;

    public void Dispose()
    {
        _disposables.Dispose();
        _authDisposables.Dispose();
    }
}
