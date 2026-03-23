using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics3D.Textures;

[Display(Name = nameof(GraphicsStrings.ImageTextureSource), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ImageTextureSource : TextureSource
{
    public ImageTextureSource()
    {
        ScanProperties<ImageTextureSource>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ImageSource?> Source { get; } = Property.Create<ImageSource?>(null);

    public partial class Resource
    {
        private ITexture2D? _gpuTexture;
        private int _gpuTextureVersion = -1;

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext)
        {
            if (Source?.Bitmap == null)
            {
                DisposeGpuTexture();
                return null;
            }

            // Check if we need to recreate the texture
            bool needsRecreate = _gpuTexture == null ||
                                 _gpuTextureVersion != Version ||
                                 _gpuTexture.Width != Source.FrameSize.Width ||
                                 _gpuTexture.Height != Source.FrameSize.Height;

            if (needsRecreate)
            {
                DisposeGpuTexture();

                using var linearBitmap = Source.Bitmap.Convert(
                    Source.Bitmap.ColorType == BitmapColorType.RgbaF16
                        ? BitmapColorType.RgbaF16
                        : BitmapColorType.Bgra8888,
                    BitmapAlphaType.Premul,
                    BitmapColorSpace.LinearSrgb);

                _gpuTexture = graphicsContext.CreateTexture2D(
                    Source.FrameSize.Width,
                    Source.FrameSize.Height,
                    linearBitmap.ColorType == BitmapColorType.RgbaF16
                        ? TextureFormat.RGBA16Float
                        : TextureFormat.BGRA8Unorm);

                // Upload pixel data
                unsafe
                {
                    var data = new ReadOnlySpan<byte>(
                        (void*)linearBitmap.Data,
                        linearBitmap.ByteCount);
                    _gpuTexture.Upload(data);
                }

                _gpuTextureVersion = Version;
            }

            return _gpuTexture;
        }

        private void DisposeGpuTexture()
        {
            _gpuTexture?.Dispose();
            _gpuTexture = null;
        }

        partial void PostDispose(bool disposing)
        {
            DisposeGpuTexture();
        }
    }
}
