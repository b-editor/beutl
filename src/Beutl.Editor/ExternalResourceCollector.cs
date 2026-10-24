using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor;

/// <summary>
/// Collects IFileSource references and font references from the project hierarchy.
/// </summary>
public sealed class ExternalResourceCollector
{
    private readonly HashSet<(Guid Object, string PropertyName, Uri OriginalUri)> _fileSources = [];
    private readonly HashSet<FontFamily> _fontFamilies = [];

    private ExternalResourceCollector()
    {
    }

    public IEnumerable<(Guid Object, string PropertyName, Uri OriginalUri)> FileSources => _fileSources;

    public IEnumerable<FontFamily> FontFamilies => _fontFamilies;

    /// <summary>
    /// Collects all resource references within the hierarchy.
    /// </summary>
    public static ExternalResourceCollector Collect(IHierarchical root, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        ExternalResourceCollector collector = new();

        CoreSerializer.Visit(root, visit =>
        {
            switch (visit.Value)
            {
                case IFileSource source when RequiresRelocation(source, projectDirectory)
                                            && GetFileReference(visit) is { } reference:
                    collector._fileSources.Add((reference.Object.Id.Value, reference.PropertyName, source.Uri));
                    break;
                case FontFamily font:
                    collector._fontFamilies.Add(font);
                    break;
            }

            if (visit.Value is CoreObject { Uri: { } uri } obj and not MediaFingerprintFile
                && IsExternalFile(uri, projectDirectory))
                collector._fileSources.Add((obj.Id.Value, "Uri", uri));
            return false;
        });

        return collector;
    }

    internal static IEnumerable<CoreObject> EnumerateObjects(IHierarchical root)
    {
        var objects = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        CoreSerializer.Visit(root, visit =>
        {
            if (visit.Value is CoreObject obj) objects.Add(obj);
            return false;
        });
        return objects;
    }

    internal static (CoreObject Object, string PropertyName)? GetFileReference(CoreSerializationVisit visit)
    {
        for (CoreSerializationVisit? parent = visit.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Value is not CoreObject owner) continue;
            string member = visit.Path[(parent.Path.Length + 1)..];
            if (owner is INodeMember && member == "Setter/Value") member = nameof(INodeMember.Property);
            return (owner, member);
        }

        return null;
    }

    /// <summary>
    /// Determines whether a source or its model dependencies need to be relocated.
    /// </summary>
    internal static bool RequiresRelocation(IFileSource source, string projectDirectory)
        => IsExternalFile(source.Uri, projectDirectory)
           || source is ModelSource model
           && model.Dependencies.Any(path => IsExternalFile(UriHelper.CreateFromPath(path), projectDirectory));

    private static bool IsExternalFile(Uri uri, string projectDirectory)
    {
        if (!uri.IsFile)
            return false;

        string filePath = uri.LocalPath;
        string fullProjectPath = Path.GetFullPath(projectDirectory);
        if (!fullProjectPath.EndsWith(Path.DirectorySeparatorChar))
            fullProjectPath += Path.DirectorySeparatorChar;

        // Different casing can name a distinct directory. Relocate even case aliases so
        // their relative references remain valid when imported on a case-sensitive volume.
        return !Path.GetFullPath(filePath).StartsWith(fullProjectPath, StringComparison.Ordinal);
    }
}
