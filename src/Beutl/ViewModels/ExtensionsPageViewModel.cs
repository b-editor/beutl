using Beutl.Api;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

namespace Beutl.ViewModels;

public sealed class ExtensionsPageViewModel : IToolWindowContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _authDisposables = [];
    private Lazy<DiscoverPageViewModel>? _discover;
    private Lazy<LibraryPageViewModel>? _library;

    public ExtensionsPageViewModel(BeutlApiApplication clients, EditorService editorService, ProjectService projectService)
    {
        clients.AuthenticatedUser.Subscribe(user =>
            {
                _authDisposables.Clear();
                _discover = new(() => new DiscoverPageViewModel(clients, editorService, projectService)
                    .DisposeWith(_authDisposables));

                _library = new(() => new LibraryPageViewModel(user, clients, editorService, projectService)
                    .DisposeWith(_authDisposables));
            })
            .DisposeWith(_disposables);
    }

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
