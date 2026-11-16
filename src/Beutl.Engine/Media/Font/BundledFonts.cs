using System.Globalization;
using System.Reflection;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Media;

// The fonts of the Beutl.BundledFonts assembly the app's UI uses, as the text fallback of the rendering engine.
// The app and the standalone MCP server both apply them, so a font without CJK or emoji coverage renders the same
// in either. The engine does not reference the assembly, which the NuGet packages leave out; a process without it
// keeps the fallback it has.
internal static class BundledFonts
{
    internal const string AssemblyName = "Beutl.BundledFonts";
    internal const string EmojiFamilyName = "Noto Color Emoji";

    // In the order of UiFonts in Beutl.Controls; the UI language's family goes first.
    private static readonly string[] s_uiFamilyNames =
    [
        "Noto Sans",
        "Noto Sans JP",
        "Noto Sans SC",
        "Noto Sans KR",
    ];

    private static readonly Lock s_gate = new();
    private static Assembly? s_assembly;
    private static bool s_loadAttempted;
    // FontManager.Instance is the only manager.
    private static bool s_registered;

    internal static string[] GetUiFamilyNames(CultureInfo culture)
    {
        string primaryFamily = culture.TwoLetterISOLanguageName switch
        {
            "ja" => "Noto Sans JP",
            "zh" => "Noto Sans SC",
            "ko" => "Noto Sans KR",
            _ => "Noto Sans",
        };

        return [.. s_uiFamilyNames.Prepend(primaryFamily).Distinct()];
    }

    // Registers the bundled faces with the manager once, opening none until text needs it, and makes them the
    // fallback in the culture's order. Fonts the project or the system registers under the same family keep the
    // styles the bundle lacks; a selected font that has a character keeps it, since fallback only fills gaps.
    internal static bool Apply(FontManager manager, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(culture);

        lock (s_gate)
        {
            Assembly? assembly = LoadAssembly();
            if (assembly is null)
                return false;

            if (!s_registered)
            {
                Register(manager, assembly);
                s_registered = true;
            }
        }

        manager.SetFallbackFonts(
            GetUiFamilyNames(culture).Select(name => new FontFamily(name)),
            new FontFamily(EmojiFamilyName));
        return true;
    }

    private static void Register(FontManager manager, Assembly assembly)
    {
        string[] resources = assembly.GetManifestResourceNames();
        foreach (string familyName in s_uiFamilyNames.Append(EmojiFamilyName))
        {
            string prefix = $"Fonts/{familyName.Replace(" ", "")}/";
            foreach (string resource in resources
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            {
                if (Path.GetExtension(resource) is not (".ttf" or ".otf"))
                    continue;

                // The file names end in the weight, such as NotoSansJP-SemiBold.otf; Noto Color Emoji has one face.
                string weightName = Path.GetFileNameWithoutExtension(resource).Split('-')[^1];
                FontWeight weight = Enum.TryParse(weightName, out FontWeight parsedWeight)
                    ? parsedWeight : FontWeight.Regular;
                manager.RegisterFont(
                    new Typeface(new FontFamily(familyName), FontStyle.Normal, weight),
                    () => assembly.GetManifestResourceStream(resource)
                        ?? throw new FileNotFoundException($"The bundled font '{resource}' is missing.", resource));
            }
        }
    }

    private static Assembly? LoadAssembly()
    {
        if (s_loadAttempted)
            return s_assembly;

        s_loadAttempted = true;
        try
        {
            s_assembly = Assembly.Load(AssemblyName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // Created here: the standalone server applies the fonts before its logging is set up.
            Log.CreateLogger(typeof(BundledFonts)).LogWarning(ex, "The bundled fonts are not available; text keeps the fallback fonts it has.");
        }

        return s_assembly;
    }
}
