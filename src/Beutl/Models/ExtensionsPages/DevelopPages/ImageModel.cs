using Avalonia.Media.Imaging;

namespace Beutl.Models.ExtensionsPages.DevelopPages;

public sealed record ImageModel(MemoryStream Stream, Bitmap Bitmap, string Name) : IDisposable
{
    public void Dispose()
    {
        Stream.Dispose();
        Bitmap.Dispose();
        GC.SuppressFinalize(this);
    }
}
