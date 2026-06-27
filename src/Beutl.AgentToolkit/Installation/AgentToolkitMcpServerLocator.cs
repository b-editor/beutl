namespace Beutl.AgentToolkit.Installation;

public static class AgentToolkitMcpServerLocator
{
    public static AgentToolkitMcpServerCommand ResolveDefault(string? baseDirectory = null)
    {
        string startDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);

        AgentToolkitMcpServerCommand? published = FindPublishedServer(startDirectory)
                                                  ?? FindPublishedServer(Path.Combine(startDirectory, "AgentToolkitMcp"));
        if (published is not null)
        {
            return published;
        }

        string? project = FindSourceProject(startDirectory);
        if (project is not null)
        {
            return new AgentToolkitMcpServerCommand(
                "dotnet",
                ["run", "--project", project],
                "source project");
        }

        return new AgentToolkitMcpServerCommand(
            "dotnet",
            ["run", "--project", "src/Beutl.AgentToolkit.Mcp/Beutl.AgentToolkit.Mcp.csproj"],
            "relative source project");
    }

    private static AgentToolkitMcpServerCommand? FindPublishedServer(string directory)
    {
        string executable = OperatingSystem.IsWindows()
            ? Path.Combine(directory, "Beutl.AgentToolkit.Mcp.exe")
            : Path.Combine(directory, "Beutl.AgentToolkit.Mcp");

        if (File.Exists(executable))
        {
            return new AgentToolkitMcpServerCommand(executable, [], "published executable");
        }

        string dll = Path.Combine(directory, "Beutl.AgentToolkit.Mcp.dll");
        if (File.Exists(dll))
        {
            return new AgentToolkitMcpServerCommand("dotnet", [dll], "published dll");
        }

        return null;
    }

    private static string? FindSourceProject(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            string project = Path.Combine(
                directory.FullName,
                "src",
                "Beutl.AgentToolkit.Mcp",
                "Beutl.AgentToolkit.Mcp.csproj");

            if (File.Exists(project))
            {
                return project;
            }
        }

        return null;
    }
}
