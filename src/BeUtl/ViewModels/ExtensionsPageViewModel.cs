using Beutl.Api;

using BeUtl.ViewModels.ExtensionsPages;

namespace BeUtl.ViewModels;

public sealed class ExtensionsPageViewModel
{
    private readonly BeutlClients _clients;

    public ExtensionsPageViewModel(BeutlClients clients)
    {
        _clients = clients;
        Develop = new DevelopPageViewModel(_clients);
    }

    public DevelopPageViewModel Develop { get; }
}
