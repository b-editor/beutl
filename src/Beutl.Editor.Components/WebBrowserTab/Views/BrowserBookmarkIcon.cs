using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

using SkiaSharp;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal sealed class BrowserBookmarkIcon : Image
{
    public static readonly StyledProperty<string?> PageUrlProperty =
        AvaloniaProperty.Register<BrowserBookmarkIcon, string?>(nameof(PageUrl));
    public static readonly StyledProperty<BrowserFaviconLoader?> LoaderProperty =
        AvaloniaProperty.Register<BrowserBookmarkIcon, BrowserFaviconLoader?>(nameof(Loader));

    private CancellationTokenSource? _loading;
    private Bitmap? _bitmap;
    private bool _attached;
    internal Task Loading { get; private set; } = Task.CompletedTask;

    public string? PageUrl
    {
        get => GetValue(PageUrlProperty);
        set => SetValue(PageUrlProperty, value);
    }

    public BrowserFaviconLoader? Loader
    {
        get => GetValue(LoaderProperty);
        set => SetValue(LoaderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == PageUrlProperty || change.Property == LoaderProperty) && _attached)
            Reload();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    private void Release()
    {
        _loading?.Cancel();
        _loading = null;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Reload()
    {
        Release();
        if (Loader is not { } loader) return;
        var cancellation = new CancellationTokenSource();
        _loading = cancellation;
        Loading = LoadAsync(loader, PageUrl, cancellation);
    }

    private async Task LoadAsync(BrowserFaviconLoader loader, string? address, CancellationTokenSource cancellation)
    {
        try
        {
            byte[]? bytes = await loader.GetAsync(address, cancellation.Token);
            if (bytes == null) return;
            Bitmap? bitmap = await Task.Run(() => Decode(bytes), cancellation.Token);
            if (!ReferenceEquals(_loading, cancellation) || cancellation.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }
            _bitmap = bitmap;
            Source = bitmap;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Keep the monogram when the site has no supported, decodable icon.
        }
        finally
        {
            if (ReferenceEquals(_loading, cancellation)) _loading = null;
            cancellation.Dispose();
        }
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        using var encoded = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(encoded);
        if (codec == null || codec.Info.Width is <= 0 or > 2048 || codec.Info.Height is <= 0 or > 2048) return null;
        using var stream = new MemoryStream(bytes, writable: false);
        return codec.Info.Width >= codec.Info.Height ? Bitmap.DecodeToWidth(stream, 48) : Bitmap.DecodeToHeight(stream, 48);
    }
}
