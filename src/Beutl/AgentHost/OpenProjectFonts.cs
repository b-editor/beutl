using Beutl.AgentToolkit.Sessions;
using Beutl.Services;

namespace Beutl.AgentHost;

// list_fonts through the live endpoint lists the fork of the active session's project, which holds its bundled fonts.
internal sealed class OpenProjectFonts(ProjectService projectService) : IOpenProjectFonts
{
    public Media.FontManager? Fonts => projectService.ProjectFonts;
}
