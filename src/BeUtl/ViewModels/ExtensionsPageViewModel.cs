using Beutl.Api;

using BeUtl.ViewModels.ExtensionsPages;

namespace BeUtl.ViewModels;

public sealed class ExtensionsPageViewModel
{
    private readonly BeutlClients _clients;
    private DevelopPageViewModel? _develop;

    public ExtensionsPageViewModel(BeutlClients clients)
    {
        _clients = clients;
        Home = new HomePageViewModel(_clients);

        _clients.AuthorizedUser.Subscribe(user =>
        {
            if (user == null)
            {
                _develop = default!;
            }
            else
            {
                _develop = new DevelopPageViewModel(user);
            }
        });
    }

    public HomePageViewModel Home { get; }

    public DevelopPageViewModel Develop
        => _develop ?? throw new Exception("Authorization is required.");
}
