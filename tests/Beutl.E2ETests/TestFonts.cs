using Beutl.Media;

namespace Beutl.E2ETests;

internal static class TestFonts
{
    private static readonly Lazy<FontManager> s_manager = new(() => new FontManager(new Beutl.Configuration.FontConfig()));

    public static FontManager Manager => s_manager.Value;
}
