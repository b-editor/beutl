using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.WebBrowserTab;

// Every web view sharing the browser profile must request the same environment: WebView2 rejects
// a user data folder that its running browser process already opened with different options.
internal static partial class BrowserWebViewEnvironment
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private static readonly Lazy<string> s_webView2UserDataFolder = new(() => PrepareWebView2UserDataFolder(
        Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebView2"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beutl", "WebView2"),
        // A moved folder keeps its permissions. Only an elevated process can move one from under Program Files,
        // and those permissions would deny the user's later unelevated runs.
        Environment.IsPrivilegedProcess ? null : Environment.ProcessPath));

    // Safari updates replace the system WebKit, so the installed Safari names the engine that WKWebView runs.
    private static readonly Lazy<string> s_safariApplicationName = new(() => GetSafariApplicationName(
        OperatingSystem.IsMacOS() ? ReadBundleVersion("/Applications/Safari.app") : null, Environment.OSVersion.Version));

    internal static void Configure(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        switch (e)
        {
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                // WKWebView omits Safari's tokens: without the product Google serves its basic HTML UI, and without
                // the version Google Drive asks for a browser update. Append both while retaining the system's OS
                // and WebKit UA.
                // EnvironmentRequested runs before the first navigation; AdapterCreated is too late.
                apple.ApplicationNameForUserAgent = s_safariApplicationName.Value;
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                // WebView2 defaults to a folder beside the executable, which an installation under
                // Program Files cannot create, and NativeWebView rethrows that failure on the dispatcher.
                windows.UserDataFolder = s_webView2UserDataFolder.Value;
                break;
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

    internal static string GetSafariApplicationName(string? safariVersion, Version osVersion)
    {
        if (!Version.TryParse(safariVersion, out Version? version))
        {
            // Safari's major version ran three ahead of macOS's until both moved to 26.
            version = new Version(osVersion.Major >= 26 ? osVersion.Major : osVersion.Major + 3, osVersion.Minor);
        }

        return $"Version/{version} Safari/605.1.15";
    }

    internal static string? ReadBundleVersion(string bundlePath)
    {
        nint path = CreateString(bundlePath);
        nint bundle = Send(Send(GetClass("NSBundle"), Selector("alloc")), Selector("initWithPath:"), path);
        Send(path, Selector("release"));
        if (bundle == 0) return null;

        nint key = CreateString("CFBundleShortVersionString");
        nint value = Send(bundle, Selector("objectForInfoDictionaryKey:"), key);
        // A string describes itself, and a value of any other type answers description instead of raising.
        string? version = value == 0
            ? null
            : Marshal.PtrToStringUTF8(Send(Send(value, Selector("description")), Selector("UTF8String")));
        Send(key, Selector("release"));
        Send(bundle, Selector("release"));
        return version;
    }

    private static nint CreateString(string value) =>
        InitString(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), value);

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)] private static partial nint InitString(nint receiver, nint selector, string value);
}
