using System.Runtime.InteropServices;
using Beutl.Api.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.ViewModels.Tools;

public sealed partial class AiJobCenterViewModel
{
    internal void SetPreviewVisibility(AiJobItemViewModel item, bool isVisible)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return;
            if (!isVisible || !_jobs.Contains(item))
            {
                _visiblePreviewItems.Remove(item);
                item.ReleasePreviewForReload();
                return;
            }

            _visiblePreviewItems.Add(item);
            TryLoadPreview_NoLock(item);
        }
    }

    private void LoadVisiblePreviews_NoLock()
    {
        foreach (AiJobItemViewModel item in _visiblePreviewItems)
            TryLoadPreview_NoLock(item);
    }

    private void TryLoadPreview_NoLock(AiJobItemViewModel item)
    {
        if (item.TryClaimPreviewLoad(out long loadGeneration))
            _ = LoadPreviewAsync(item, loadGeneration);
    }

    private async Task LoadPreviewAsync(AiJobItemViewModel item, long loadGeneration)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;

        bool enteredGate = false;
        try
        {
            await _previewLoadGate.WaitAsync(lifetimeOperation.CancellationToken);
            enteredGate = true;
            lock (_lifetimeGate)
            {
                if (_isDisposed
                    || !_visiblePreviewItems.Contains(item)
                    || !item.IsPreviewLoadCurrent(loadGeneration))
                {
                    item.ResetPreviewLoadClaim(loadGeneration);
                    return;
                }
            }
            if (item.ContentUri is not { } contentUri)
            {
                item.ResetPreviewLoadClaim(loadGeneration);
                return;
            }

            using var buffer = new SizeLimitedMemoryStream(
                checked((int)AiRequestLimits.MaxImageUploadBytes));
            await _content.CopyToAsync(contentUri, buffer, lifetimeOperation.CancellationToken);
            buffer.Position = 0;
            Bitmap preview = await Task.Run(
                () => DecodePreview(buffer),
                lifetimeOperation.CancellationToken);
            item.SetPreview(Ref<Bitmap>.Create(preview), loadGeneration);
        }
        catch (OperationCanceledException) when (lifetimeOperation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The card still names the job; only the picture beside it is missing.
            _logger.LogDebug(ex, "Failed to load a preview for AI job {JobId}", item.Id);
            item.ResetPreviewLoadClaim(loadGeneration);
        }
        finally
        {
            if (enteredGate)
                _previewLoadGate.Release();
        }
    }

    internal static Bitmap DecodePreview(Stream encodedContent)
    {
        ArgumentNullException.ThrowIfNull(encodedContent);
        using SKCodec codec = SKCodec.Create(encodedContent)
            ?? throw new InvalidDataException("Failed to inspect the AI preview image.");
        SKImageInfo sourceInfo = codec.Info;
        AiImageDecodeValidator.Validate(sourceInfo);

        const int maxDimension = 512;
        double scale = Math.Min(1d, Math.Min(
            maxDimension / (double)sourceInfo.Width,
            maxDimension / (double)sourceInfo.Height));
        int width = Math.Max(1, (int)Math.Round(sourceInfo.Width * scale));
        int height = Math.Max(1, (int)Math.Round(sourceInfo.Height * scale));
        var decodeInfo = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        if (codec.StartScanlineDecode(decodeInfo) != SKCodecResult.Success)
        {
            // Some codecs, notably interlaced PNG, cannot expose scanlines. Keep
            // their fallback surface far below the validated server maximum so
            // four concurrent previews still have a deterministic memory bound.
            if ((long)sourceInfo.Width * sourceInfo.Height > 4_194_304)
            {
                throw new InvalidDataException(
                    "The AI preview image is too large for its decoder.");
            }

            using SKBitmap source = SKBitmap.Decode(codec)
                ?? throw new InvalidDataException("Failed to decode the AI preview image.");
            using SKBitmap resized = source.Resize(
                new SKImageInfo(width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                ?? throw new InvalidDataException("Failed to resize the AI preview image.");
            return EncodePreview(resized);
        }

        int sourceRowBytes = checked(sourceInfo.Width * 4);
        byte[] sourceRow = new byte[sourceRowBytes];
        byte[] thumbnailPixels = new byte[checked(width * height * 4)];
        GCHandle rowHandle = GCHandle.Alloc(sourceRow, GCHandleType.Pinned);
        try
        {
            int nextSourceRow = 0;
            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Min(
                    sourceInfo.Height - 1,
                    (int)(((long)(2 * y + 1) * sourceInfo.Height) / (2L * height)));
                int rowsToSkip = sourceY - nextSourceRow;
                if (rowsToSkip > 0 && !codec.SkipScanlines(rowsToSkip))
                    throw new InvalidDataException("Failed to seek within the AI preview image.");
                if (codec.GetScanlines(rowHandle.AddrOfPinnedObject(), 1, sourceRowBytes) != 1)
                    throw new InvalidDataException("Failed to decode the AI preview image.");
                nextSourceRow = sourceY + 1;

                int targetRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Min(
                        sourceInfo.Width - 1,
                        (int)(((long)(2 * x + 1) * sourceInfo.Width) / (2L * width)));
                    Buffer.BlockCopy(sourceRow, sourceX * 4, thumbnailPixels, targetRow + x * 4, 4);
                }
            }
        }
        finally
        {
            rowHandle.Free();
        }

        using var thumbnail = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        Marshal.Copy(thumbnailPixels, 0, thumbnail.GetPixels(), thumbnailPixels.Length);
        return EncodePreview(thumbnail);
    }

    private static Bitmap EncodePreview(SKBitmap thumbnail)
    {
        using SKImage image = SKImage.FromBitmap(thumbnail);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream();
        png.SaveTo(stream);
        stream.Position = 0;
        return Bitmap.FromStream(stream);
    }
}
