using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media.Source;

namespace Beutl.Editor;

/// <summary>
/// Lists the local files a file source reads.
/// </summary>
internal static class FileSourceFiles
{
    public static IEnumerable<string> EnumerateReferencedPaths(object value)
    {
        if (value is CoreObject { Uri: { IsAbsoluteUri: true, IsFile: true } uri }) yield return uri.LocalPath;
        if (value is FileInfo file) yield return file.FullName;
        if (value is IFileSource source)
        {
            foreach (string path in EnumerateLocalPaths(source)) yield return path;
        }
    }

    public static IEnumerable<string> EnumerateLocalPaths(IFileSource source)
    {
        // Media and model sources throw from Uri until they have read a file.
        if (source is MediaSource { HasUri: false } or ModelSource { HasUri: false }
            || source.Uri is not { IsAbsoluteUri: true, IsFile: true })
            return [];

        // A model also reads the textures and other files its own file names.
        return source is ModelSource model ? [source.Uri.LocalPath, .. model.Dependencies, .. model.MissingTextures] : [source.Uri.LocalPath];
    }
}
