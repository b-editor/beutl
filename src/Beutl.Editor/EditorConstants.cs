namespace Beutl.Editor;

/// <summary>
/// Constants shared between the editor non-UI layer (services) and the
/// editor UI layer (Components / Beutl). The Components-side
/// <c>Beutl.Editor.Components.Constants</c> mirrors these so existing
/// public references keep working; new code should reach for this type.
/// </summary>
public static class EditorConstants
{
    public const string ElementFileExtension = "belm";
}
