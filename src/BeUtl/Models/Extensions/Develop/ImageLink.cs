using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
        FirebaseStorageReference reference = service._storage.Child(path)
            .Child(name);

        var stream = new MemoryStream(jpeg);
        var link = new ImageLink(path, name, stream);
        await reference.PutAsync(stream, default, mimeType: "image/jpeg");

        return link;
    }

    //新規の場合ダウンロードし弱参照でキャッシュする
    public async ValueTask<Bitmap?> TryGetBitmapAsync()
    {
        try
        {
            if (!_bitmap.TryGetTarget(out Bitmap? bitmap))
            {
                AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
                HttpClient client = ServiceLocator.Current.GetRequiredService<HttpClient>();
                FirebaseStorageReference reference = service._storage.Child(Path)
                    .Child(Name);

                if (_stream == null)
                {
                    string? downloadLink = await reference.GetDownloadUrlAsync();

                    if (downloadLink == null)
                    {
                        return null;
                    }
                    _stream = new MemoryStream(await client.GetByteArrayAsync(downloadLink));
                }

                bitmap = new Bitmap(_stream);
                _bitmap.SetTarget(bitmap);
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
        FirebaseStorageReference reference = service._storage.Child(Path)
            .Child(Name);
        return await reference.GetDownloadUrlAsync() != null;
    }

    public async ValueTask DeleteAsync()
    {
        AccountService service = ServiceLocator.Current.GetRequiredService<AccountService>();
        FirebaseStorageReference reference = service._storage.Child(Path)
            .Child(Name);
        await reference.DeleteAsync();
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
