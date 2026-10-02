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
        // One upload per content kind, so a source bound to a color and a data slot does not re-upload every draw.
        private readonly CachedTexture[] _gpuTextures = new CachedTexture[2];

        // A decoded bitmap has a fixed pixel count, so surfaceDensity is ignored here —
        // unlike DrawableTextureSource, whose vector content re-rasterizes at the surface density.
        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
        {
            return GetTexture(graphicsContext, surfaceDensity, TextureContentKind.Color);
        }

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity, TextureContentKind contentKind)
        {
            if (Source?.Bitmap == null)
            {
                DisposeGpuTextures();
                return null;
            }

            ref CachedTexture cached = ref _gpuTextures[(int)contentKind];

            // Check if we need to recreate the texture
            bool needsRecreate = cached.Texture == null ||
                                 !ReferenceEquals(cached.Context, graphicsContext) ||
                                 cached.Version != Version ||
                                 cached.Texture.Width != Source.FrameSize.Width ||
                                 cached.Texture.Height != Source.FrameSize.Height;

            if (needsRecreate)
            {
                cached.Texture?.Dispose();
                cached.Texture = null;

                using var uploadBitmap = CreateUploadBitmap(Source.Bitmap, contentKind);

                var texture = graphicsContext.CreateTexture2D(
                    Source.FrameSize.Width,
                    Source.FrameSize.Height,
                    uploadBitmap.ColorType == BitmapColorType.RgbaF16
                        ? TextureFormat.RGBA16Float
                        : TextureFormat.BGRA8Unorm);

                // Upload pixel data; the texture reaches the cache only once it holds them, so a failed upload must
                // release it here.
                try
                {
                    unsafe
                    {
                        var data = new ReadOnlySpan<byte>(
                            (void*)uploadBitmap.Data,
                            uploadBitmap.ByteCount);
                        texture.Upload(data);
                    }
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }

                cached.Texture = texture;
                cached.Context = graphicsContext;
                cached.Version = Version;
            }

            return cached.Texture;
        }

        /// <summary>
        /// Converts a decoded bitmap into the pixels uploaded for <paramref name="contentKind"/>, either
        /// <see cref="BitmapColorType.RgbaF16"/> or <see cref="BitmapColorType.Bgra8888"/>.
        /// </summary>
        /// <remarks>
        /// Colors decode to linear sRGB in half floats: 8 bits cannot hold the decoded shadows, which would band.
        /// Data keeps its stored values: the bitmap's own color space is the destination, so no transfer function is
        /// applied, and it stays unpremultiplied because each channel is an independent value rather than a color.
        /// </remarks>
        internal static Bitmap CreateUploadBitmap(Bitmap source, TextureContentKind contentKind)
        {
            if (contentKind == TextureContentKind.Data)
            {
                if (source.ColorType == BitmapColorType.Srgba8888)
                {
                    // Srgba8888 decodes sRGB when read, so read its stored bytes as plain Rgba8888 instead.
                    using var stored = new Bitmap(
                        source.Data, source.Width, source.Height, source.RowBytes,
                        BitmapColorType.Rgba8888, source.AlphaType, source.ColorSpace);
                    return stored.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
                }

                return source.Convert(
                    HasAtMost8BitsPerChannel(source.ColorType) ? BitmapColorType.Bgra8888 : BitmapColorType.RgbaF16,
                    BitmapAlphaType.Unpremul,
                    source.ColorSpace);
            }

            return source.Convert(BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        }

        private static bool HasAtMost8BitsPerChannel(BitmapColorType colorType)
        {
            return colorType is BitmapColorType.Alpha8 or BitmapColorType.Rgb565 or BitmapColorType.Argb4444
                or BitmapColorType.Rgba8888 or BitmapColorType.Rgb888x or BitmapColorType.Bgra8888
                or BitmapColorType.Gray8 or BitmapColorType.Rg88 or BitmapColorType.R8Unorm;
        }

        private void DisposeGpuTextures()
        {
            for (int i = 0; i < _gpuTextures.Length; i++)
            {
                _gpuTextures[i].Texture?.Dispose();
                _gpuTextures[i] = default;
            }
        }

        partial void PostDispose(bool disposing)
        {
            DisposeGpuTextures();
        }

        private struct CachedTexture
        {
            public ITexture2D? Texture;
            public IGraphicsContext? Context;
            public int Version;
        }
    }
}
