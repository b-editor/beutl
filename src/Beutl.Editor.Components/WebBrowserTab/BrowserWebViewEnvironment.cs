using System.Runtime.Versioning;

using Avalonia.Controls;
using Avalonia.Platform;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.WebBrowserTab;

// Every web view sharing the browser profile must request the same environment: WebView2 rejects
// a user data folder that its running browser process already opened with different options.
internal static class BrowserWebViewEnvironment
{
    private static readonly Lazy<string> s_webView2UserDataFolder = new(() => PrepareWebView2UserDataFolder(
        Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebView2"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beutl", "WebView2"),
        // A moved folder keeps its permissions. Only an elevated process can move one from under Program Files,
        // and those permissions would deny the user's later unelevated runs.
        Environment.IsPrivilegedProcess ? null : Environment.ProcessPath));

    internal static void Configure(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        switch (e)
        {
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                // WKWebView omits Safari's product token, so Google serves its basic HTML UI.
                // Append the compatibility token while retaining the system's OS and WebKit UA.
                // EnvironmentRequested runs before the first navigation; AdapterCreated is too late.
                apple.ApplicationNameForUserAgent = "Safari/605.1.15";
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                // WebView2 defaults to a folder beside the executable, which an installation under
                // Program Files cannot create, and NativeWebView rethrows that failure on the dispatcher.
                windows.UserDataFolder = s_webView2UserDataFolder.Value;
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                // Embedding WebKitGTK as a child X11 window is unstable. Render it offscreen and composite
                // its frames like the WPE adapter does; Ubuntu ships no WPE WebKit, so GTK is its only engine.
                gtk.ExperimentalOffscreen = true;
                break;
        }
    }

    // WebView2 keeps cookies in its profile folder, but WebKitGTK and WPE keep them in memory unless the
    // application names a store, so restarting Beutl would sign the user out of every site.
    internal static void PersistCookies(object? sender, WebViewAdapterEventArgs e)
    {
        if (OperatingSystem.IsLinux() && e.TryGetPlatformHandle() is { } handle) _ = PersistCookiesAsync(handle);
    }

    [SupportedOSPlatform("linux")]
    private static async Task PersistCookiesAsync(IPlatformHandle handle)
    {
        string path = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebKit", "cookies.sqlite");
        try
        {
            // The store holds the user's sign-ins. Like a browser profile, only the user may enter its folder,
            // which also covers the SQLite journal files WebKit creates beside it.
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory, ownerOnly);
            File.SetUnixFileMode(directory, ownerOnly);
            await LinuxWebKitCookies.UsePersistentStorageAsync(handle, path);
        }
        catch (Exception ex)
        {
            // Cookies then last until Beutl exits, as they did before.
            Log.CreateLogger(typeof(BrowserWebViewEnvironment)).LogWarning(ex, "Could not keep WebKit cookies in {Path}.", path);
        }
    }

    // Earlier versions left the profile in WebView2's default folder; moving it keeps the user's sign-ins.
    internal static string PrepareWebView2UserDataFolder(string folder, string fallbackFolder, string? processPath,
        Action<string, string>? moveDirectory = null)
    {
        string? legacyFolder = processPath == null ? null : processPath + ".WebView2";
        try
        {
            if (!Directory.Exists(folder) && Directory.Exists(legacyFolder))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
                (moveDirectory ?? Directory.Move)(legacyFolder, folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A running instance still holds it, it is on another volume, or the installation directory
            // is read-only. The browser starts with a new profile instead.
            Log.CreateLogger(typeof(BrowserWebViewEnvironment)).LogWarning(ex,
                "Could not move the WebView2 user data folder from {LegacyFolder} to {Folder}.", legacyFolder, folder);
        }

        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // WebView2 could not create the profile there either, and that failure would end the application.
            Log.CreateLogger(typeof(BrowserWebViewEnvironment)).LogWarning(ex,
                "Could not create the WebView2 user data folder {Folder}; using {FallbackFolder} instead.",
                folder, fallbackFolder);
            return fallbackFolder;
        }
    }
}
