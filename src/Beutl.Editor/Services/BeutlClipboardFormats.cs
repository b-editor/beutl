namespace Beutl.Editor.Services;

/// <summary>
/// Format identifiers used on the platform clipboard for Beutl-specific
/// payloads. Mirrored from <c>Beutl.Editor.Components.BeutlDataFormats</c>
/// without the Avalonia <c>DataFormat&lt;T&gt;</c> wrapper, so services in
/// <c>Beutl.Editor</c> can dispatch on them without taking a UI dependency.
/// </summary>
public static class BeutlClipboardFormats
{
    public const string Element = "BeutlElementJson";

    public const string Elements = "BeutlElementsJson";

    public const string Files = "Files";

    public const string Bitmap = "Bitmap";
}
