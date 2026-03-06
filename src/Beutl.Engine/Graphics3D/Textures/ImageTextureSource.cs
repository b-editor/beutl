using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Language;
using Beutl.Media.Source;

namespace Beutl.Graphics3D.Textures;

[Display(Name = nameof(Strings.ImageTextureSource), ResourceType = typeof(Strings))]
public sealed partial class ImageTextureSource : TextureSource
{
    public ImageTextureSource()
    {
        ScanProperties<ImageTextureSource>();
    }

    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
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

                _gpuTexture = graphicsContext.CreateTexture2D(
                    Source.FrameSize.Width,
                    Source.FrameSize.Height,
                    TextureFormat.BGRA8Unorm);

                // Upload pixel data
                unsafe
                {
                    var data = new ReadOnlySpan<byte>(
                        (void*)Source.Bitmap.Data,
                        Source.Bitmap.ByteCount);
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
