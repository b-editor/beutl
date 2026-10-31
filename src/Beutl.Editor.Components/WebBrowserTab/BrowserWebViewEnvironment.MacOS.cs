using static Beutl.Editor.Components.Helpers.ObjCRuntime;

namespace Beutl.Editor.Components.WebBrowserTab;

internal static partial class BrowserWebViewEnvironment
{
    internal static string? ReadBundleVersion(string bundlePath)
    {
        nint path = NewString(bundlePath);
        nint bundle = Send(Send(GetClass("NSBundle"), Selector("alloc")), Selector("initWithPath:"), path);
        Send(path, Selector("release"));
        if (bundle == 0) return null;

        nint key = NewString("CFBundleShortVersionString");
        nint value = Send(bundle, Selector("objectForInfoDictionaryKey:"), key);
        // A string describes itself, and a value of any other type answers description instead of raising.
        string? version = value == 0
            ? null
            : ToManagedString(Send(value, Selector("description")));
        Send(key, Selector("release"));
        Send(bundle, Selector("release"));
        return version;
    }
}
