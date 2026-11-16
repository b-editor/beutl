using System.Globalization;
using Beutl.Media;

namespace Beutl.AgentToolkit.Rendering;

/// <summary>
/// Gives the headless MCP server the text fallback the app sets up at startup: the bundled UI families in the
/// UI language's order, then Noto Color Emoji, so a selected font without CJK or emoji coverage renders the same
/// in render_still and export_video as in the editor. Faces load when text first needs them.
/// </summary>
internal static class BundledFontRegistration
{
    // Registers the faces once per process; applying the same order again changes nothing.
    public static void EnsureRegistered()
    {
        // The editor sets CurrentUICulture from its language setting; this server follows the system's,
        // which is the setting's default.
        BundledFonts.Apply(FontManager.Instance, CultureInfo.CurrentUICulture);
    }
}
