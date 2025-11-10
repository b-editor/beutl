namespace Beutl.IO;

public class LocalFileSystem : IFileSystem
{
    private static string GetFullPath(Uri uri)
    {
        return uri.IsAbsoluteUri ? uri.LocalPath : throw new ArgumentException("The URI must be absolute.", nameof(uri));
    }

    public Uri[] GetFiles(Uri uri, string searchPattern, bool recursive)
    {
        return Directory.EnumerateFiles(GetFullPath(uri), searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Select(path => new Uri(path))
            .ToArray();
    }

    public Uri[] GetDirectory(Uri uri, string searchPattern, bool recursive)
    {
        return Directory.EnumerateDirectories(GetFullPath(uri), searchPattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Select(path => new Uri(path))
            .ToArray();
    }

    public bool FileExists(Uri uri)
    {
        return File.Exists(GetFullPath(uri));
    }

    public bool DirectoryExists(Uri uri)
    {
        return Directory.Exists(GetFullPath(uri));
    }

    public void CreateDirectory(Uri uri)
    {
        Directory.CreateDirectory(GetFullPath(uri));
    }

    public void DeleteFile(Uri uri)
    {
        File.Delete(GetFullPath(uri));
    }

    public void DeleteDirectory(Uri uri, bool recursive)
    {
        Directory.Delete(GetFullPath(uri), recursive);
    }

    public Stream CreateFile(Uri uri)
    {
        return File.Create(GetFullPath(uri));
    }

    public Stream OpenFile(Uri uri)
    {
        return File.Open(GetFullPath(uri), FileMode.Create, FileAccess.ReadWrite);
    }
}
