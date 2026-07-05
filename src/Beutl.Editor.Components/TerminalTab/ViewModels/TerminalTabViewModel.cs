using System.Text.Json.Nodes;

using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Editor.Components.TerminalTab.ViewModels;

public sealed class TerminalTabViewModel : IToolContext
{
    public TerminalTabViewModel(IEditorContext editorContext)
    {
        WorkingDirectory = ResolveWorkingDirectory(editorContext);
        (ShellPath, ShellArgs) = ResolveShell(
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());
        LangFallback = ResolveLangFallback(
            Environment.GetEnvironmentVariable,
            CultureInfo.CurrentCulture.Name,
            OperatingSystem.IsWindows());
    }

    public ToolTabExtension Extension => TerminalTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public string Header => Strings.Terminal;

    public string ShellPath { get; }

    public string[] ShellArgs { get; }

    public string? WorkingDirectory { get; }

    public string? LangFallback { get; }

    public ReactivePropertySlim<bool> IsProcessExited { get; } = new();

    public ReactivePropertySlim<int> ExitCode { get; } = new();

    internal event EventHandler? Disposed;

    internal static (string Path, string[] Args) ResolveShell(
        Func<string, string?> getEnvironmentVariable, bool isWindows, bool isMacOS)
    {
        if (isWindows)
        {
            return (getEnvironmentVariable("COMSPEC") ?? "cmd.exe", []);
        }

        string? shell = getEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell))
        {
            shell = isMacOS ? "/bin/zsh" : "/bin/bash";
        }

        // A login shell picks up the user's profile (PATH etc.), which a bare PTY shell would not.
        return (shell, ["-l"]);
    }

    internal static string? ResolveLangFallback(
        Func<string, string?> getEnvironmentVariable, string cultureName, bool isWindows)
    {
        if (isWindows)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(getEnvironmentVariable("LC_ALL"))
            || !string.IsNullOrEmpty(getEnvironmentVariable("LC_CTYPE"))
            || !string.IsNullOrEmpty(getEnvironmentVariable("LANG")))
        {
            return null;
        }

        // GUI-launched macOS/Linux apps carry no locale in their environment, which drops
        // shells and TUI apps into the C locale and garbles multi-byte output.
        return cultureName.Contains('-')
            ? cultureName.Replace('-', '_') + ".UTF-8"
            : "en_US.UTF-8";
    }

    internal static string? ResolveWorkingDirectory(IEditorContext editorContext)
    {
        Scene? scene = editorContext.GetService<Scene>();
        Project? project = scene?.FindHierarchicalParent<Project>();
        if (project?.Uri is { } projectUri
            && Path.GetDirectoryName(projectUri.LocalPath) is { Length: > 0 } projectDirectory)
        {
            return projectDirectory;
        }

        if (scene?.Uri is { } sceneUri
            && Path.GetDirectoryName(sceneUri.LocalPath) is { Length: > 0 } sceneDirectory)
        {
            return sceneDirectory;
        }

        return null;
    }

    public void Dispose()
    {
        Disposed?.Invoke(this, EventArgs.Empty);
        Disposed = null;
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType) => null;
}
