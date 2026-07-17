using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Extensibility;
using DynamicData;
using FluentAvalonia.Styling;

namespace Beutl.Helpers;

public static class OutProcessDialog
{
    public static IDisposable Show(
        string? title,
        string? subtitle,
        string? content,
        string? icon,
        bool progress = false,
        bool closable = false)
    {
        var startInfo = new ProcessStartInfo();
        DotNetProcess.Configure(startInfo, Path.Combine(AppContext.BaseDirectory, "Beutl.WaitingDialog"));

        startInfo.ArgumentList.AddRange(["--parent", Environment.ProcessId.ToString()]);

        if (!string.IsNullOrWhiteSpace(title))
            startInfo.ArgumentList.AddRange(["--title", title]);

        if (!string.IsNullOrWhiteSpace(subtitle))
            startInfo.ArgumentList.AddRange(["--subtitle", subtitle]);

        if (!string.IsNullOrWhiteSpace(content))
            startInfo.ArgumentList.AddRange(["--content", content]);

        if (!string.IsNullOrWhiteSpace(icon))
            startInfo.ArgumentList.AddRange(["--icon", icon]);

        if (progress)
            startInfo.ArgumentList.Add("--progress");

        if (closable)
            startInfo.ArgumentList.Add("--closable");

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        if (viewConfig.Theme != BuiltinThemeIds.System)
        {
            // The waiting dialog runs in a separate process with no ThemeRegistry/extensions, so
            // translate the theme id to a base variant it understands. Custom themes fall back to
            // their descriptor's BaseVariant; "system" is excluded above and stays OS-following.
            string themeArg = ThemeRegistry.ResolveOrDefault(viewConfig.Theme) is { } descriptor
                ? ToThemeArg(descriptor)
                : "auto";
            startInfo.ArgumentList.AddRange(["--theme", themeArg]);
        }

        var process = Process.Start(startInfo);

        void ProcessExit()
        {
            process?.Kill();
            process?.Dispose();
            process = null;
        }

        return Disposable.Create(ProcessExit);
    }

    private static string ToThemeArg(ThemeDescriptor descriptor)
    {
        if (descriptor.IsSystemFollowing)
        {
            return "auto";
        }

        if (descriptor.BaseVariant == ThemeVariant.Light)
        {
            return "light";
        }

        if (descriptor.BaseVariant == ThemeVariant.Dark)
        {
            return "dark";
        }

        if (descriptor.BaseVariant == FluentAvaloniaTheme.HighContrastTheme)
        {
            return "highcontrast";
        }

        return "dark";
    }
}
