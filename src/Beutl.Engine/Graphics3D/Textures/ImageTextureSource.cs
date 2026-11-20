using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics3D.Textures;

[Display(Name = nameof(GraphicsStrings.ImageTextureSource), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ImageTextureSource : TextureSource
{
    public ImageTextureSource()
    {
        ScanProperties<ImageTextureSource>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ImageSource?> Source { get; } = Property.CreateExpression<ImageSource?>(null);

    public partial class Resource
    {
        private const int StraightColorSlot = 2;

        // One upload per content kind, so a source bound to a color and a data slot does not re-upload every draw.
        private readonly CachedTexture[] _gpuTextures = new CachedTexture[3];
        private int? _versionWithoutStraightColors;

        // A decoded bitmap has a fixed pixel count, so surfaceDensity is ignored for raster images,
        // while an SVG re-rasterizes its vector drawing at the surface density.
        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity = 1f)
        {
            return GetTexture(graphicsContext, surfaceDensity, TextureContentKind.Color);
        }

        public override ITexture2D? GetTexture(IGraphicsContext graphicsContext, float surfaceDensity, TextureContentKind contentKind)
        {
            if (Source == null)
            {
                DisposeGpuTextures();
                return null;
            }

            if (Source.Picture != null)
            {
                float sanitizedDensity = RenderScaleUtilities.SanitizeOutputScale(surfaceDensity);
                int maxDimension = BufferDimensionBudget.ForDevice(graphicsContext).MaxDimension;
                PixelSize targetSize = Source.ResolveSvgRasterSize(sanitizedDensity, maxDimension);
                return GetCachedTexture(
                    graphicsContext,
                    (int)contentKind,
                    targetSize,
                    sanitizedDensity,
                    (Source, targetSize, contentKind),
                    static state => CreateSvgUploadBitmap(state.Source, state.targetSize, state.contentKind));
            }

            if (Source.Bitmap is not { } bitmap)
            {
                DisposeGpuTextures();
                return null;
            }

            return GetCachedTexture(
                graphicsContext,
                (int)contentKind,
                Source.FrameSize,
                1f,
                (bitmap, contentKind),
                static state => CreateUploadBitmap(state.bitmap, state.contentKind));
        }

        internal override ITexture2D? GetStraightColorTexture(IGraphicsContext graphicsContext)
        {
            if (_versionWithoutStraightColors == Version)
                return null;

            if (Source?.Picture != null)
            {
                _versionWithoutStraightColors = Version;
                return null;
            }

            if (Source?.Bitmap is not { } sourceBitmap)
            {
                DisposeGpuTextures();
                _versionWithoutStraightColors = Version;
                return null;
            }

            ITexture2D? texture = GetCachedTexture(
                graphicsContext,
                StraightColorSlot,
                Source.FrameSize,
                1f,
                (Source, sourceBitmap),
                static state =>
                {
                    using Bitmap? decoded = state.Source.DecodeUnpremultiplied();
                    // A file replaced since it was loaded can hold another size than the texture is made for.
                    return decoded?.Width == state.sourceBitmap.Width && decoded.Height == state.sourceBitmap.Height
                        ? CreateStraightColorUploadBitmap(decoded)
                        : null;
                });
            if (texture == null)
                _versionWithoutStraightColors = Version;

            return texture;
        }

        internal override bool TryGetAlpha(Vector2 texCoord, out float alpha, float? surfaceDensity = null)
        {
            if (Source?.Picture is { } picture)
                return TryGetPictureAlpha(picture, Source, texCoord, ResolveHitTestSvgRasterSize(Source, surfaceDensity), out alpha);

            if (Source?.Bitmap is not { } bitmap)
                return base.TryGetAlpha(texCoord, out alpha, surfaceDensity);

            // Filtered and clamped to the edge, as the materials sample their maps.
            float x = texCoord.X * bitmap.Width - 0.5f;
            float y = texCoord.Y * bitmap.Height - 0.5f;
            int left = (int)MathF.Floor(x);
            int top = (int)MathF.Floor(y);
            alpha = float.Lerp(
                float.Lerp(ReadAlpha(bitmap, left, top), ReadAlpha(bitmap, left + 1, top), x - left),
                float.Lerp(ReadAlpha(bitmap, left, top + 1), ReadAlpha(bitmap, left + 1, top + 1), x - left),
                y - top);
            return true;
        }

        private PixelSize ResolveHitTestSvgRasterSize(ImageSource.Resource source, float? surfaceDensity)
        {
            ref readonly CachedTexture cachedColor = ref _gpuTextures[(int)TextureContentKind.Color];
            if (cachedColor.Texture != null && cachedColor.Version == Version)
            {
                if (surfaceDensity is null
                    || RenderScaleUtilities.SanitizeOutputScale(surfaceDensity.Value) == cachedColor.Density)
                {
                    return new PixelSize(cachedColor.Texture.Width, cachedColor.Texture.Height);
                }
            }

            IGraphicsContext? context = cachedColor.Context ?? GraphicsContextFactory.SharedContext;
            int maxDimension = BufferDimensionBudget.ForDevice(context).MaxDimension;
            return source.ResolveSvgRasterSize(surfaceDensity ?? 1f, maxDimension);
        }

        private static bool TryGetPictureAlpha(
            SKPicture picture,
            ImageSource.Resource source,
            Vector2 texCoord,
            PixelSize rasterSize,
            out float alpha)
        {
            PixelSize frameSize = source.FrameSize;
            if (frameSize.Width <= 0 || frameSize.Height <= 0 || rasterSize.Width <= 0 || rasterSize.Height <= 0)
            {
                alpha = 1;
                return false;
            }

            // Rasterize only the 2x2 texel neighborhood at the rendered texture's resolution and filter linearly
            // with clamp-to-edge, matching how the material samples the uploaded texture.
            float x = texCoord.X * rasterSize.Width - 0.5f;
            float y = texCoord.Y * rasterSize.Height - 0.5f;
            int left = (int)MathF.Floor(x);
            int top = (int)MathF.Floor(y);
            int x0 = Math.Clamp(left, 0, rasterSize.Width - 1);
            int x1 = Math.Clamp(left + 1, 0, rasterSize.Width - 1);
            int y0 = Math.Clamp(top, 0, rasterSize.Height - 1);
            int y1 = Math.Clamp(top + 1, 0, rasterSize.Height - 1);
            int patchWidth = x1 - x0 + 1;
            int patchHeight = y1 - y0 + 1;

            using var surface = SKSurface.Create(new SKImageInfo(patchWidth, patchHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Translate(-x0, -y0);
            surface.Canvas.Scale((float)rasterSize.Width / frameSize.Width, (float)rasterSize.Height / frameSize.Height);
            surface.Canvas.ClipRect(SKRect.Create(frameSize.Width, frameSize.Height));
            surface.Canvas.DrawPicture(picture);
            using SKPixmap pixels = surface.PeekPixels();
            float a00 = pixels.GetPixelColor(0, 0).Alpha / 255f;
            float a10 = pixels.GetPixelColor(x1 - x0, 0).Alpha / 255f;
            float a01 = pixels.GetPixelColor(0, y1 - y0).Alpha / 255f;
            float a11 = pixels.GetPixelColor(x1 - x0, y1 - y0).Alpha / 255f;
            alpha = float.Lerp(
                float.Lerp(a00, a10, x - left),
                float.Lerp(a01, a11, x - left),
                y - top);
            return true;
        }

        private static float ReadAlpha(Bitmap bitmap, int x, int y)
            => bitmap.SKBitmap.GetPixel(Math.Clamp(x, 0, bitmap.Width - 1), Math.Clamp(y, 0, bitmap.Height - 1)).Alpha / 255f;

        private ITexture2D? GetCachedTexture<TState>(
            IGraphicsContext graphicsContext,
            int slot,
            PixelSize targetSize,
            float density,
            TState state,
            Func<TState, Bitmap?> createUpload)
        {
            ref CachedTexture cached = ref _gpuTextures[slot];

            bool needsRecreate = cached.Texture == null ||
                                 !ReferenceEquals(cached.Context, graphicsContext) ||
                                 cached.Version != Version ||
                                 cached.Texture.Width != targetSize.Width ||
                                 cached.Texture.Height != targetSize.Height;

            if (needsRecreate)
            {
                cached.Texture?.Dispose();
                cached.Texture = null;

                using Bitmap? uploadBitmap = createUpload(state);
                if (uploadBitmap == null)
                    return null;

                var texture = graphicsContext.CreateTexture2D(
                    uploadBitmap.Width,
                    uploadBitmap.Height,
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
                cached.Density = density;
            }

            return cached.Texture;
        }

        private static Bitmap? CreateSvgUploadBitmap(
            ImageSource.Resource source,
            PixelSize targetSize,
            TextureContentKind contentKind)
        {
            if (contentKind == TextureContentKind.Color)
            {
                return source.RasterizePicture(
                    targetSize,
                    BitmapColorType.RgbaF16,
                    BitmapAlphaType.Premul,
                    BitmapColorSpace.LinearSrgb);
            }

            // Skia cannot draw directly to an unpremultiplied canvas, so rasterize premultiplied sRGB first
            // and convert to unpremultiplied BGRA8 for data maps.
            using Bitmap? rasterized = source.RasterizePicture(
                targetSize,
                BitmapColorType.Bgra8888,
                BitmapAlphaType.Premul,
                BitmapColorSpace.Srgb);
            return rasterized is null ? null : CreateUploadBitmap(rasterized, TextureContentKind.Data);
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

        /// <summary>Converts unpremultiplied pixels to linear sRGB in half floats, keeping the colors under alpha 0.</summary>
        internal static Bitmap CreateStraightColorUploadBitmap(Bitmap source)
        {
            // Not Bitmap.Convert: a draw goes through premultiplied colors.
            var upload = new SKBitmap(new SKImageInfo(
                source.Width, source.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul, SKColorSpace.CreateSrgbLinear()));
            using SKPixmap destination = upload.PeekPixels();
            using SKPixmap pixels = source.SKBitmap.PeekPixels();
            using var tagged = new SKPixmap(
                pixels.Info.WithColorSpace(SKColorSpace.CreateSrgb()), pixels.GetPixels(), pixels.RowBytes);
            if (!tagged.ReadPixels(destination))
            {
                upload.Dispose();
                throw new InvalidOperationException("Failed to convert the image's colors for upload.");
            }

            return Bitmap.FromFixedLayout(upload);
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
            public float Density;
        }
    }
}
