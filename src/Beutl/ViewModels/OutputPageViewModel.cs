using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.ViewModels;

public sealed class OutputPageViewModel : IPageContext
{
    public PageExtension Extension => OutputPageExtension.Instance;

    public string Header => Strings.Output;

    public void Dispose()
    {
    }
}
