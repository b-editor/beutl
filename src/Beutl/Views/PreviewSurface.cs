using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.ViewModels;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

// Only the image and its letterbox. Editor transforms and overlays belong to PlayerView.
internal sealed class PreviewSurface : Border, IDisposable
{
    private readonly IDisposable _hdrSubscription;

    public PreviewSurface(PlayerViewModel player)
    {
        DataContext = player;
        Background = Brushes.Black;
        ClipToBounds = true;
        _hdrSubscription = GlobalConfiguration.Instance.EditorConfig.UseHdrPreview.GetObservable()
            .ObserveOnUIDispatcher()
            .Subscribe(useHdr =>
            {
                ClearImage();
                Child = CreateImage(useHdr, "CleanPreviewImage.Value");
                Child.HorizontalAlignment = HorizontalAlignment.Center;
                Child.VerticalAlignment = VerticalAlignment.Center;
            });
    }

    internal static Control CreateImage(bool useHdr, string sourcePath)
    {
        Control image = useHdr ? new HdrBitmapView() : new BitmapView();
        image.Bind(useHdr ? HdrBitmapView.SourceProperty : BitmapView.SourceProperty,
            new ReflectionBinding(sourcePath) { Mode = BindingMode.OneWay });
        image.Bind(useHdr ? HdrBitmapView.ToneMappingProperty : BitmapView.ToneMappingProperty,
            new ReflectionBinding("ToneMappingMode.Value") { Mode = BindingMode.OneWay });
        image.Bind(useHdr ? HdrBitmapView.ToneMappingExposureProperty : BitmapView.ToneMappingExposureProperty,
            new ReflectionBinding("ToneMappingExposure.Value") { Mode = BindingMode.OneWay });
        return image;
    }

    private void ClearImage()
    {
        if (Child is HdrBitmapView hdr)
            hdr.ClearValue(HdrBitmapView.SourceProperty);
        else if (Child is BitmapView sdr)
            sdr.ClearValue(BitmapView.SourceProperty);
        Child = null;
    }

    public void Dispose()
    {
        _hdrSubscription.Dispose();
        ClearImage();
        DataContext = null;
    }
}
