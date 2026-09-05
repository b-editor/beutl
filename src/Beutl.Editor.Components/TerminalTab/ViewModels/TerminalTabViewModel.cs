using System.Text.Json.Nodes;

using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Editor.Components.TerminalTab.ViewModels;

public sealed class TerminalTabViewModel : IToolContext
{
    // Number instances; shell and working directory are not unique, so numbers are not persisted.
    private static int s_lastInstanceNumber;

    private readonly ReadOnlyReactivePropertySlim<string> _header;

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
        InstanceNumber = Interlocked.Increment(ref s_lastInstanceNumber);

        _header = TerminalTitle
            .Select(BuildHeader)
            .ToReadOnlyReactivePropertySlim(BuildHeader(TerminalTitle.Value))!;
    }

    public ToolTabExtension Extension => TerminalTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public int InstanceNumber { get; }

    /// <summary>Gets the title reported by the shell through OSC 0/2.</summary>
    /// <remarks>macOS shells may omit it, so the numbered title is the fallback.</remarks>
    public ReactivePropertySlim<string?> TerminalTitle { get; } = new();

    public IReadOnlyReactiveProperty<string> Header => _header;

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
        // POSIX locale names are language[_TERRITORY]; keep the language and the ISO-3166 territory
        // (a two-letter uppercase subtag), skipping any script subtag such as "Hant" in zh-Hant-TW,
        // which would otherwise produce an invalid locale like zh_Hant_TW.UTF-8.
        string[] parts = cultureName.Split('-');
        string? region = Array.FindLast(parts, static p => p.Length == 2 && p.All(char.IsAsciiLetterUpper));
        return parts.Length > 0 && !string.IsNullOrEmpty(parts[0]) && region is not null
            ? $"{parts[0]}_{region}.UTF-8"
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

    public ValueTask DisposeAsync()
    {
        Disposed?.Invoke(this, EventArgs.Empty);
        Disposed = null;
        IsSelected.Dispose();
        IsProcessExited.Dispose();
        ExitCode.Dispose();
        _header.Dispose();
        TerminalTitle.Dispose();
        return ValueTask.CompletedTask;
    }


    private string BuildHeader(string? reportedTitle)
    {
        string numbered = $"{Strings.Terminal} {InstanceNumber.ToString(CultureInfo.CurrentCulture)}";
        return string.IsNullOrWhiteSpace(reportedTitle)
            ? numbered
            : $"{numbered}: {reportedTitle.Trim()}";
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public object? GetService(Type serviceType) => null;
}
