using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Sessions;

public sealed class ProjectConflictException : Exception
{
    public ProjectConflictException(string path)
        : base($"The project file has changed since it was opened: {path}")
    {
        Path = path;
    }

    public string Code => ErrorCode.ProjectConflict;

    public string Path { get; }
}
