namespace Beutl.AgentToolkit.Sessions;

// The fonts of the project a host has open, for a call bound to no session, such as list_fonts through the live
// editor endpoint, whose sessions are bound per scene.
public interface IOpenProjectFonts
{
    // Null while no project is open.
    Media.FontManager? Fonts { get; }
}
