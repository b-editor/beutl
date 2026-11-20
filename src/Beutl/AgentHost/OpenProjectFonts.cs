using Beutl.AgentToolkit.Sessions;
using Beutl.Services;

namespace Beutl.AgentHost;

// list_fonts through the live endpoint lists the open project's fork, which holds its bundled fonts.
internal sealed class OpenProjectFonts(ProjectService projectService) : IOpenProjectFonts
{
    public Media.FontManager? Fonts => projectService.ProjectFonts;
}
