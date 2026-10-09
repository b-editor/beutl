namespace Beutl;

/// <summary>
/// Specifies a file type offered by the file picker for a file source or <see cref="FileInfo"/> property.
/// </summary>
/// <remarks>
/// Every platform's picker reads <see cref="Patterns"/>, and the Windows picker reads nothing else.
/// When patterns are given they alone decide which files can be dropped onto the editor.
/// A filter without patterns matches dropped files by extension against a built-in list of common
/// media types and refuses files outside it, so give patterns for custom file types.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class FileFilterAttribute(string name, params string[] patterns) : Attribute
{
    public string Name { get; } = name;

    public string[] Patterns { get; } = patterns;

    public string[]? MimeTypes { get; set; }

    public string[]? AppleUniformTypeIdentifiers { get; set; }
}
