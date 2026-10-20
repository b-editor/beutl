using Beutl.Controls.Navigation;

namespace Beutl.ViewModels.ExtensionsPages;

public abstract class BasePageViewModel : PageContext, IDisposable
{
    public abstract void Dispose();
}
