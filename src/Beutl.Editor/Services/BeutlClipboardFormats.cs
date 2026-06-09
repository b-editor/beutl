namespace Beutl.Editor.Services;

/// <summary>
/// Clipboard format identifiers for Beutl payloads. Mirrors
/// <c>Beutl.Editor.Components.BeutlDataFormats</c> without the Avalonia
/// <c>DataFormat&lt;T&gt;</c> wrapper, so <c>Beutl.Editor</c> services stay
/// UI-free.
/// </summary>
public static class BeutlClipboardFormats
{
    public const string Element = "BeutlElementJson";

    public const string Elements = "BeutlElementsJson";

    public const string Files = "Files";

    public const string Bitmap = "Bitmap";

    /// <summary>
    /// Plain-text payload, mapped to the platform's native text slot so a Copy
    /// that exposes JSON also leaves a human-pasteable string for other apps.
    /// </summary>
    public const string Text = "text/plain";
}
