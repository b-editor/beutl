using Avalonia;
using Avalonia.Markup.Xaml;

namespace Beutl.E2ETests;

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
