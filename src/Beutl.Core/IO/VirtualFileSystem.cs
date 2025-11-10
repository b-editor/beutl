namespace Beutl.IO;

public class VirtualFileSystem : IFileSystem
{
    public Dictionary<Uri, byte[]> Files { get; } = new();

    public Uri[] GetFiles(Uri uri, string searchPattern, bool recursive)
    {
        string pattern = searchPattern.Replace("*", ".*").Replace("?", ".");
        var regex = new System.Text.RegularExpressions.Regex($"^{pattern}$");
        string uriString = uri.ToString();

        return Files.Keys
            .Select(u => (Uri: u, UriString: u.ToString()))
            .Where(t => t.UriString.StartsWith(uriString))
            .Select(t => (Name: t.UriString.Substring(uriString.Length).TrimStart('/'), t.Uri))
            .Where(t => regex.IsMatch(t.Name))
            .Select(u => u.Uri)
            .ToArray();
    }

    public Uri[] GetDirectory(Uri uri, string searchPattern, bool recursive)
    {
        // 仮想ファイルシステムではディレクトリはサポートしないため、空の配列を返す
        return Array.Empty<Uri>();
    }

    public bool FileExists(Uri uri)
    {
        return Files.ContainsKey(uri);
    }

    public bool DirectoryExists(Uri uri)
    {
        // 仮想ファイルシステムではディレクトリはサポートしないため、常にfalseを返す
        return false;
    }

    public void CreateDirectory(Uri uri)
    {
        // 仮想ファイルシステムではディレクトリはサポートしないため、何もしない
    }

    public void DeleteFile(Uri uri)
    {
        Files.Remove(uri);
    }

    public void DeleteDirectory(Uri uri, bool recursive)
    {
        // 仮想ファイルシステムではディレクトリはサポートしないため、何もしない
    }

    public Stream CreateFile(Uri uri)
    {
        if (!Files.TryGetValue(uri, out var data))
        {
            Files[uri] = data = Array.Empty<byte>();
        }

        return new VirtualFileStream(data, data2 => Files[uri] = data2);
    }

    public Stream OpenFile(Uri uri)
    {
        if (Files.TryGetValue(uri, out var data))
        {
            return new VirtualFileStream(data, data2 => Files[uri] = data2);
        }
        else
        {
            throw new FileNotFoundException("File not found in virtual file system.", uri.ToString());
        }
    }

    private sealed class VirtualFileStream : MemoryStream
    {
        private readonly Action<byte[]> _onClose;

        public VirtualFileStream(byte[] data, Action<byte[]> onClose)
        {
            Write(data);
            Position = 0;
            _onClose = onClose;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _onClose(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
