namespace Beutl.AgentToolkit.Installation;

public static class AgentToolkitMcpServerLocator
{
    // Returns null when neither a shipped binary nor a source checkout is
    // found — installs must then surface "stdio unavailable" instead of
    // writing a `dotnet run` command that cannot work on the user's machine.
    // baseDirectory is a test hook; production uses AppContext.BaseDirectory.
    public static AgentToolkitMcpServerCommand? ResolveDefault(string? baseDirectory = null)
    {
        string startDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);

        // Beutl's own builds (nukebuild/Build.cs Publish and the macOS bundle) put the server next to the app.
        AgentToolkitMcpServerCommand? published = FindPublishedServer(startDirectory);
        if (published is not null)
        {
            return published;
        }

        string? project = FindSourceProject(startDirectory);
        if (project is not null)
        {
            return new AgentToolkitMcpServerCommand(
                "dotnet",
                ["run", "--project", project]);
        }

        return null;
    }

    private static AgentToolkitMcpServerCommand? FindPublishedServer(string directory)
    {
        string executable = OperatingSystem.IsWindows()
            ? Path.Combine(directory, "Beutl.AgentToolkit.Mcp.exe")
            : Path.Combine(directory, "Beutl.AgentToolkit.Mcp");

        if (File.Exists(executable))
        {
            return new AgentToolkitMcpServerCommand(executable, []);
        }

        string dll = Path.Combine(directory, "Beutl.AgentToolkit.Mcp.dll");
        if (File.Exists(dll))
        {
            return new AgentToolkitMcpServerCommand("dotnet", [dll]);
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
