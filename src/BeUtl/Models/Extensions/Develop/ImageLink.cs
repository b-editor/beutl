using Avalonia.Media.Imaging;

using BeUtl.Services;

using Firebase.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Models.Extensions.Develop;

public sealed class ImageLink : IDisposable, IEquatable<ImageLink?>
{
    private readonly WeakReference<Bitmap> _bitmap = new(null!);
    private MemoryStream? _stream;

    private ImageLink(string path, string name)
    {
        Path = path;
        Name = name;
    }

    private ImageLink(string path, string name, MemoryStream stream)
    {
        Path = path;
        Name = name;
        _stream = stream;
    }

    public string Path { get; }

    public string Name { get; }

    public static ImageLink Open(string path, string name)
    {
        return new ImageLink(path, name);
    }

    public static async ValueTask<ImageLink> UploadAsync(string path, string name, byte[] jpeg)
    {
        AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
        FirebaseStorageReference reference = service._storage.Child(path).Child(name);

        var stream = new MemoryStream(jpeg);
        var link = new ImageLink(path, name, stream);
        await reference.PutAsync(stream, default, mimeType: "image/jpeg");

        return link;
    }

    public async ValueTask<MemoryStream?> TryGetStreamAsync()
    {
        try
        {
            if (_stream == null)
            {
                AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
                HttpClient client = ServiceLocator.Current.GetRequiredService<HttpClient>();
                FirebaseStorageReference reference = service._storage.Child(Path).Child(Name);
                string? downloadLink = await reference.GetDownloadUrlAsync();

                if (downloadLink == null)
                {
                    return null;
                }
                _stream = new MemoryStream(await client.GetByteArrayAsync(downloadLink));
            }

            if (_stream != null)
            {
                _stream.Position = 0;
            }

            return _stream;
        }
        catch
        {
            return null;
        }
    }

    //新規の場合ダウンロードし弱参照でキャッシュする
    public async ValueTask<Bitmap?> TryGetBitmapAsync()
    {
        try
        {
            if (!_bitmap.TryGetTarget(out Bitmap? bitmap))
            {
                MemoryStream? stream = await TryGetStreamAsync();
                if (stream != null)
                {
                    bitmap = new Bitmap(stream);
                    _bitmap.SetTarget(bitmap);
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<bool> IsExistsAsync()
    {
        AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
        FirebaseStorageReference reference = service._storage.Child(Path).Child(Name);

        try
        {
            return await reference.GetDownloadUrlAsync() != null;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DeleteAsync()
    {
        AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
        FirebaseStorageReference reference = service._storage.Child(Path).Child(Name);

        if (await IsExistsAsync())
        {
            await reference.DeleteAsync();
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public override bool Equals(object? obj) => Equals(obj as ImageLink);

    public bool Equals(ImageLink? other) => other is not null && Path == other.Path && Name == other.Name;

    public override int GetHashCode() => HashCode.Combine(Path, Name);

    public static bool operator ==(ImageLink? left, ImageLink? right) => EqualityComparer<ImageLink>.Default.Equals(left, right);

    public static bool operator !=(ImageLink? left, ImageLink? right) => !(left == right);
}
