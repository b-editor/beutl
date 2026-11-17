using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.AgentToolkit.Tests;

internal static class TestFonts
{
    private static readonly Lazy<FontManager> s_manager = new(Create);

    public static FontManager Manager => s_manager.Value;

    private static FontManager Create()
    {
        var configuration = new FontConfig();
        return new FontManager(configuration);
    }
}
