using Avalonia.Web.Blazor;

namespace BeUtl.Web;

public partial class App
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        
        WebAppBuilder.Configure<BeUtl.App>()
            .SetupWithSingleViewLifetime();
    }
}
