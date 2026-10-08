namespace Beutl;

/// <summary>
/// Specifies a file type offered by the file picker for a file source or <see cref="FileInfo"/> property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class FileFilterAttribute(string name, params string[] patterns) : Attribute
{
    public string Name { get; } = name;

    public string[] Patterns { get; } = patterns;

    public string[]? MimeTypes { get; set; }

    public string[]? AppleUniformTypeIdentifiers { get; set; }
}
