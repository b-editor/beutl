using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Microsoft.Extensions.Logging;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;

using Beutl.Embedding.MediaFoundation;
using Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
using Beutl.Extensions.MediaFoundation;
using Beutl.Extensions.MediaFoundation.Decoding;
#endif

#pragma warning disable CA1416 // Validate platform compatibility (SupportedOSPlatform on the Extension).

[SupportedOSPlatform("windows")]
public class MFEncodingController : EncodingController
{
    private const int AudioFrameSize = 1024;
    // 1 second in Media Foundation's 100-ns unit base.
    private const long HnsPerSecond = 10_000_000L;

    private readonly ILogger _logger = Log.CreateLogger<MFEncodingController>();

    public MFEncodingController(string outputFile) : base(outputFile) { }

    public override MFVideoEncoderSettings VideoSettings { get; } = new();

    public override MFAudioEncoderSettings AudioSettings { get; } = new();

    public override async ValueTask Encode(
        IFrameProvider frameProvider,
        ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        // Media Foundation mandates a per-thread COM apartment + MFStartup. The shared
        // dispatcher already owns that lifecycle for decoding — encoders join the same
        // thread to match MFReader's COM/DXVA2 model. We pass a Func<Task> overload so
        // awaits inside EncodeCore yield the dispatcher between RenderFrame/Sample
        // pumps; sync-over-async (.GetAwaiter().GetResult()) on this thread would
        // deadlock if a continuation tried to post back to it.
        await MFThread.Dispatcher.InvokeAsync(() =>
            EncodeCore(frameProvider, sampleProvider, cancellationToken));
    }

    private async Task EncodeCore(
        IFrameProvider frameProvider,
        ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        bool isHdr = VideoSettings.IsHdr;
        bool audioOnly = IsAudioOnlyContainer(OutputFile);

        // Write to a sibling temp file then rename on success. If the encode aborts
        // (cancellation, codec error, exception in RenderFrame), the partially
        // written file is deleted instead of overwriting the user's previous output.
        //
        // The temp file MUST keep the original extension as its suffix because
        // MFCreateSinkWriterFromURL picks the container muxer from the file
        // name — `clip.mp4.partial` would resolve to no recognized muxer and
        // fail before writing begins. `clip.partial.mp4` preserves `.mp4` at
        // the end so Sink Writer selects the MP4 sink.
        string tempFile = BuildTempOutputPath(OutputFile);
        try { if (File.Exists(tempFile)) File.Delete(tempFile); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not remove stale temp file {Temp}", tempFile); }

        bool encodeCompleted = false;
        try
        {
            using IMFAttributes sinkAttrs = MediaFactory.MFCreateAttributes(1);
            // Asks Sink Writer to prefer hardware encoder MFTs (QSV / NVENC / AMD VCE)
            // when available. If no compatible hardware MFT is installed, MF
            // transparently falls back to the software encoder — we log the
            // resolved MFT identity after BeginWriting so the user can see which
            // path was chosen.
            sinkAttrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1);

            using IMFSinkWriter sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(tempFile, null, sinkAttrs);

            // Audio-only containers (.wav/.mp3/.aac/.adts/.m4a) cannot mux a video stream;
            // adding one here makes BeginWriting fail. Pure-audio outputs run through the
            // audio path only.
            int videoStreamIndex = audioOnly ? -1 : ConfigureVideoStream(sinkWriter, isHdr);
            int audioChannels = ResolveAudioChannels();
            int sampleRate = ResolveOutputSampleRate((int)sampleProvider.SampleRate);
            MFAudioEncoderSettings.AudioCodecType audioCodec = ResolveAudioCodec();
            int audioStreamIndex = ConfigureAudioStream(sinkWriter, audioCodec, sampleRate, audioChannels);

            // SDR path needs the per-stream color space resolved from user selection,
            // not a hard-coded sRGB conversion — otherwise the pixels we hand the
            // encoder don't match the BT.709/BT.601/Rec.2020 tag we write into the
            // output stream and players show a slightly off-grade SDR image.
            BitmapColorSpace? hdrTargetColorSpace = null;
            BitmapColorSpace sdrSourceColorSpace = BitmapColorSpace.Srgb;
            if (!audioOnly)
            {
                if (isHdr)
                {
                    hdrTargetColorSpace = MFColorSpaceHelper.BuildHdrColorSpace(
                        MapTransferForHelper(VideoSettings.ColorTransfer, isHdr: true),
                        MapPrimariesForHelper(VideoSettings.ColorPrimaries, isHdr: true));
                }
                else
                {
                    sdrSourceColorSpace = MFColorSpaceHelper.BuildTargetColorSpace(
                        MapTransferForHelper(VideoSettings.ColorTransfer, isHdr: false),
                        MapPrimariesForHelper(VideoSettings.ColorPrimaries, isHdr: false));
                }
            }
            // SDR NV12 conversion must match the matrix we tag on the output stream.
            PixelFormatConverter.YuvMatrix8 sdrMatrix = ResolveSdrYuvMatrix(VideoSettings.YCbCrMatrix);

            sinkWriter.BeginWriting();
            LogEncoderActivation(sinkWriter, videoStreamIndex);

            Exception? loopFailure = null;
            try
            {
                long frameCount = 0;
                long sampleCount = 0;
                bool encodeVideo = videoStreamIndex >= 0;
                bool encodeAudio = audioStreamIndex >= 0;

                long frameRateNum = VideoSettings.FrameRate.Numerator;
                long frameRateDen = VideoSettings.FrameRate.Denominator;

                while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
                {
                    long videoTs = (encodeVideo && frameRateNum > 0)
                        ? frameCount * frameRateDen * HnsPerSecond / frameRateNum
                        : long.MaxValue;
                    long audioTs = (encodeAudio && sampleRate > 0)
                        ? sampleCount * HnsPerSecond / sampleRate
                        : long.MaxValue;

                    if (encodeVideo && videoTs <= audioTs)
                    {
                        await WriteVideoFrame(sinkWriter, videoStreamIndex, frameProvider,
                            frameCount, frameRateNum, frameRateDen,
                            hdrTargetColorSpace, sdrSourceColorSpace, isHdr, sdrMatrix);
                        frameCount++;
                        if (frameCount >= frameProvider.FrameCount)
                        {
                            encodeVideo = false;
                        }
                    }
                    else if (encodeAudio)
                    {
                        await WriteAudioFrame(sinkWriter, audioStreamIndex, sampleProvider,
                            sampleCount, AudioFrameSize, sampleRate, audioChannels);
                        sampleCount += AudioFrameSize;
                        if (sampleCount >= sampleProvider.SampleCount)
                        {
                            encodeAudio = false;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                encodeCompleted = !cancellationToken.IsCancellationRequested;
            }
            catch (Exception ex)
            {
                loopFailure = ex;
                throw;
            }
            finally
            {
                // Finalize flushes encoder queues and writes the container footer. Skipping
                // this produces a partial file that most demuxers refuse to open.
                // If the encode loop already threw, prefer surfacing that exception —
                // re-throwing a Finalize failure here would hide the real cause.
                try
                {
                    sinkWriter.Finalize();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IMFSinkWriter.Finalize failed");
                    if (loopFailure == null)
                    {
                        throw;
                    }
                }
            }

            if (encodeCompleted)
            {
                // Single-syscall replace on Windows / Linux so we never leave a window
                // where the prior output has been deleted but the new one is not yet
                // in place. A previous Delete + Move sequence could lose the user's
                // existing file if the Move failed mid-way (permission, sharing).
                File.Move(tempFile, OutputFile, overwrite: true);
            }
        }
        finally
        {
            if (!encodeCompleted)
            {
                // Encode failed or was cancelled — drop the partial container so it
                // doesn't accumulate next to the real output.
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Could not delete partial output {Temp}; user may need to remove it manually",
                        tempFile);
                }
            }
        }
    }

    // GUIDs from mfapi.h — Vortice does not expose these as named constants, but
    // they're stable Win32 attribute keys on the encoder MFT's IMFAttributes.
    private static readonly Guid MFT_FRIENDLY_NAME_Attribute = new("314FFBAE-5B41-4C95-9C19-4E7D586FACE3");
    private static readonly Guid MFT_ENUM_HARDWARE_URL_Attribute = new("2FB866AC-B078-4942-AB6C-003D05CDA674");

    // Surfaces whether the Sink Writer activated a hardware or software encoder MFT
    // for the video stream. Even with MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS=1 set,
    // MF silently picks the software encoder when no compatible hardware MFT is
    // installed; this introspection makes the chosen path visible in the logs so
    // users investigating slow encodes can confirm the cause.
    private void LogEncoderActivation(IMFSinkWriter sinkWriter, int videoStreamIndex)
    {
        if (videoStreamIndex < 0) return;
        try
        {
            nint transformPtr = sinkWriter.GetServiceForStream(
                videoStreamIndex, Guid.Empty, typeof(IMFTransform).GUID);
            using var transform = new IMFTransform(transformPtr);
            using IMFAttributes attrs = transform.Attributes;

            string? friendlyName = null;
            try { friendlyName = attrs.GetString(MFT_FRIENDLY_NAME_Attribute); }
            catch { /* attribute optional */ }

            bool isHardware = false;
            try
            {
                _ = attrs.GetString(MFT_ENUM_HARDWARE_URL_Attribute);
                isHardware = true;
            }
            catch { /* hardware attribute absent on software MFTs */ }

            _logger.LogInformation(
                "Video encoder MFT for stream {Stream}: {Name} ({Kind})",
                videoStreamIndex,
                friendlyName ?? "(unnamed)",
                isHardware ? "hardware" : "software (fallback)");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Could not introspect encoder MFT for stream {Stream}", videoStreamIndex);
        }
    }

    private int ConfigureVideoStream(IMFSinkWriter sinkWriter, bool isHdr)
    {
        int width = VideoSettings.DestinationSize.Width;
        int height = VideoSettings.DestinationSize.Height;
        int bitrate = VideoSettings.Bitrate;
        long frNum = VideoSettings.FrameRate.Numerator;
        long frDen = VideoSettings.FrameRate.Denominator;

        // HDR implies HEVC Main10 even if the user left Codec at H264 / Default.
        // AV1 / H.264 have no defined HDR10 path in Media Foundation.
        Guid outSubType;
        bool useHevcMain10 = isHdr || VideoSettings.Codec == MFVideoEncoderSettings.VideoCodec.HEVC;
        if (isHdr && VideoSettings.Codec != MFVideoEncoderSettings.VideoCodec.HEVC)
        {
            // Surface the implicit promotion so users notice their Codec selection
            // was overridden by the HDR transfer characteristic.
            _logger.LogWarning(
                "HDR transfer ({Transfer}) selected — promoting codec from {Codec} to HEVC Main10. " +
                "H.264 has no Media Foundation HDR10 path.",
                VideoSettings.ColorTransfer, VideoSettings.Codec);
        }
        if (useHevcMain10)
        {
            outSubType = VideoFormatGuids.Hevc;
        }
        else
        {
            outSubType = VideoFormatGuids.H264;
        }

        // Output (compressed) media type.
        using IMFMediaType outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outType.Set(MediaTypeAttributeKeys.Subtype, outSubType);
        outType.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
        outType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // Progressive
        outType.Set(MediaTypeAttributeKeys.FrameSize, PackUint64((uint)width, (uint)height));
        outType.Set(MediaTypeAttributeKeys.FrameRate, PackUint64((uint)frNum, (uint)frDen));
        outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackUint64(1, 1));

        // MF_MT_MPEG2_PROFILE is reused by MF for both H.264 (eAVEncH264VProfile) and
        // HEVC (eAVEncH265VProfile). HDR → force Main10; otherwise honor user selection.
        if (useHevcMain10)
        {
            int hevcProfile = isHdr
                ? (int)MFVideoEncoderSettings.HevcProfileType.Main10
                : (int)VideoSettings.HevcProfile;
            outType.Set(MediaTypeAttributeKeys.Mpeg2Profile, hevcProfile);
        }
        else
        {
            outType.Set(MediaTypeAttributeKeys.Mpeg2Profile, (int)VideoSettings.H264Profile);
        }

        // Set color tags on the output media type so downstream demuxers/players
        // can reproduce the intended grade — this is what tells HDR TVs to switch
        // modes, and SDR viewers to apply BT.709 instead of defaulting to sRGB.
        ApplyColorTags(outType, isHdr);

        int streamIndex = sinkWriter.AddStream(outType);

        // Input (uncompressed) media type: P010 for HDR, NV12 for SDR.
        using IMFMediaType inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inType.Set(MediaTypeAttributeKeys.Subtype, isHdr ? VideoFormatGuids.P010 : VideoFormatGuids.NV12);
        inType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // Progressive
        inType.Set(MediaTypeAttributeKeys.FrameSize, PackUint64((uint)width, (uint)height));
        inType.Set(MediaTypeAttributeKeys.FrameRate, PackUint64((uint)frNum, (uint)frDen));
        inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackUint64(1, 1));
        // Tell MF the actual byte stride of the buffers we hand it. For odd
        // widths (e.g. 853) the chroma row needs ceil(width/2)*2 bytes, which
        // is one byte wider than width — without this attribute the encoder
        // MFT computes stride from FrameSize and reads each row off-by-one.
        int chromaWidth = ((width + 1) / 2) * 2;
        int defaultStride = isHdr ? chromaWidth * 2 : chromaWidth;
        inType.Set(MediaTypeAttributeKeys.DefaultStride, defaultStride);
        ApplyColorTags(inType, isHdr);

        sinkWriter.SetInputMediaType(streamIndex, inType, null);
        return streamIndex;
    }

    private void ApplyColorTags(IMFMediaType mediaType, bool isHdr)
    {
        // Defaults fire in when the user picks "Default" in the UI: for HDR we need
        // Rec.2020/PQ (HDR10) unconditionally; for SDR we leave the field unset so
        // the decoder picks its own default (typically BT.709 for HD, BT.601 for SD).
        VideoTransferFunction trc = MapTransferToMF(VideoSettings.ColorTransfer, isHdr);
        VideoPrimaries primaries = MapPrimariesToMF(VideoSettings.ColorPrimaries, isHdr);
        VideoTransferMatrix matrix = MapMatrixToMF(VideoSettings.YCbCrMatrix, isHdr);

        if (trc != VideoTransferFunction.FuncUnknown)
            mediaType.Set(MediaTypeAttributeKeys.TransferFunction, (uint)trc);
        if (primaries != VideoPrimaries.Unknown)
            mediaType.Set(MediaTypeAttributeKeys.VideoPrimaries, (uint)primaries);
        if (matrix != VideoTransferMatrix.Unknown)
            mediaType.Set(MediaTypeAttributeKeys.YuvMatrix, (uint)matrix);
    }

    // The renderer always hands us Pcm<Stereo32BitFloat>. We can faithfully emit mono
    // (downmix L+R) or stereo (passthrough); anything wider than 2 has no source data
    // to fill it, so clamping is safer than declaring a layout we cannot populate.
    private int ResolveAudioChannels()
    {
        int resolved = ClampAudioChannels(AudioSettings.Channels, out bool clamped);
        if (clamped)
        {
            // Visible warning instead of a silent clamp — a user asking for 5.1
            // should at least see why they got stereo so they can adjust the UI
            // (or change the upstream sample provider).
            _logger.LogWarning(
                "AudioSettings.Channels {Requested} not supported by the Stereo32BitFloat source; clamping to {Resolved}",
                AudioSettings.Channels, resolved);
        }
        return resolved;
    }

    // Pure-logic split of ResolveAudioChannels so the clamp rules can be
    // unit-tested without instantiating MFEncodingController (which requires
    // the MF runtime on Windows). Mirrors the contract documented on
    // ResolveAudioChannels — keep the WHY description in one place to avoid
    // the two copies drifting apart over time.
    internal static int ClampAudioChannels(int requested, out bool clamped)
    {
        int resolved;
        if (requested <= 0) resolved = 2;
        else if (requested >= 2) resolved = 2;
        else resolved = 1;
        clamped = resolved != requested;
        return resolved;
    }

    // Insert a `.partial` marker before the original extension so the temp
    // file keeps the container-identifying suffix that Sink Writer needs:
    //   /tmp/clip.mp4 → /tmp/clip.partial.mp4
    //   /tmp/track    → /tmp/track.partial
    internal static string BuildTempOutputPath(string outputFile)
    {
        string dir = Path.GetDirectoryName(outputFile) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(outputFile);
        string ext = Path.GetExtension(outputFile);
        string tempName = string.IsNullOrEmpty(ext)
            ? nameWithoutExt + ".partial"
            : nameWithoutExt + ".partial" + ext;
        return Path.Combine(dir, tempName);
    }

    internal static bool IsAudioOnlyContainer(string outputFile)
    {
        string ext = Path.GetExtension(outputFile);
        return ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".adts", StringComparison.OrdinalIgnoreCase);
    }

    // Pcm.Resamples recomputes its fractional position from zero on every call,
    // so resampling per 1024-sample chunk would inject sample-aligned clicks
    // and accumulate drift. Until a stateful resampler (e.g. the MF Audio
    // Resampler DSP) is wired up we honor the source rate and surface the
    // override decision so the user notices their setting was ignored.
    private int ResolveOutputSampleRate(int sourceSampleRate)
    {
        int requested = AudioSettings.SampleRate;
        if (requested > 0 && requested != sourceSampleRate)
        {
            _logger.LogWarning(
                "AudioSettings.SampleRate {Requested} differs from source {Source}; using source rate to avoid resampling artifacts",
                requested, sourceSampleRate);
        }
        return sourceSampleRate;
    }

    // Audio codec must match what the chosen container can mux. WAV requires
    // PCM, MP3 requires MP3, and ASF/WMV requires WMA — overriding here is
    // friendlier than a cryptic AddStream/BeginWriting failure, but the user
    // needs visibility into the substitution so they can adjust UI selections
    // (e.g. bitrate) that no longer apply.
    private MFAudioEncoderSettings.AudioCodecType ResolveAudioCodec()
    {
        string ext = Path.GetExtension(OutputFile);
        MFAudioEncoderSettings.AudioCodecType requested = AudioSettings.Codec;
        MFAudioEncoderSettings.AudioCodecType resolved;

        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            resolved = MFAudioEncoderSettings.AudioCodecType.PCM;
        else if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            resolved = MFAudioEncoderSettings.AudioCodecType.MP3;
        else if (ext.Equals(".wma", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".asf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase))
            resolved = MFAudioEncoderSettings.AudioCodecType.WMA;
        else if (ext.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".adts", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            // Raw AAC / ADTS streams (and AAC-in-MP4 audio-only) must carry an
            // AAC payload — Sink Writer cannot mux MP3 / WMA / PCM into these
            // containers, so the configured subtype has to be AAC even if the
            // user picked something else in the UI.
            resolved = MFAudioEncoderSettings.AudioCodecType.AAC;
        else
            resolved = requested;

        if (resolved != requested)
        {
            _logger.LogInformation(
                "Audio codec overridden from {Requested} to {Resolved} for container {Ext}",
                requested, resolved, ext);
        }
        return resolved;
    }

    private int ConfigureAudioStream(
        IMFSinkWriter sinkWriter,
        MFAudioEncoderSettings.AudioCodecType codec,
        int sampleRate,
        int channels)
    {
        if (sampleRate <= 0)
        {
            // The source must report a positive sample rate; silently returning -1
            // would drop the audio track from the output file without telling the
            // user, so we fail loudly instead.
            throw new InvalidOperationException(
                $"Cannot configure audio stream: resolved sample rate is {sampleRate}. " +
                "The sample provider must report a positive SampleRate.");
        }

        using IMFMediaType outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);

        switch (codec)
        {
            case MFAudioEncoderSettings.AudioCodecType.MP3:
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Mp3);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
            case MFAudioEncoderSettings.AudioCodecType.WMA:
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.WMAudioV9);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
            case MFAudioEncoderSettings.AudioCodecType.PCM:
                // Uncompressed 16-bit little-endian — no bitrate field needed.
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                outType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                outType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(channels * 2));
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * channels * 2));
                break;
            default: // AAC
                outType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                outType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
                outType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSettings.Bitrate / 8);
                break;
        }

        outType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, sampleRate);
        outType.Set(MediaTypeAttributeKeys.AudioNumChannels, channels);

        int streamIndex = sinkWriter.AddStream(outType);

        // Input: 16-bit signed PCM. The MF AAC encoder MFT only supports this input
        // shape — Stereo32BitFloat (our internal working format) must be downmixed.
        using IMFMediaType inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        inType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        inType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
        inType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, sampleRate);
        inType.Set(MediaTypeAttributeKeys.AudioNumChannels, channels);
        inType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(channels * 2));
        inType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * channels * 2));

        sinkWriter.SetInputMediaType(streamIndex, inType, null);
        return streamIndex;
    }

    private static async ValueTask WriteVideoFrame(
        IMFSinkWriter sinkWriter, int streamIndex,
        IFrameProvider frameProvider,
        long frame, long frameRateNum, long frameRateDen,
        BitmapColorSpace? hdrTargetColorSpace,
        BitmapColorSpace sdrSourceColorSpace,
        bool isHdr,
        PixelFormatConverter.YuvMatrix8 sdrMatrix)
    {
        using Bitmap image = await frameProvider.RenderFrame(frame);

        long ptsHns = frameRateNum > 0
            ? frame * frameRateDen * HnsPerSecond / frameRateNum
            : 0;
        long durationHns = frameRateNum > 0
            ? frameRateDen * HnsPerSecond / frameRateNum
            : HnsPerSecond / 30;

        if (isHdr && hdrTargetColorSpace is not null)
        {
            // 16-bit RGBA in HDR target color space → P010 (10-bit YUV 4:2:0 MSB-aligned).
            // The HDR path always converts via BT.2020 NCL limited-range coefficients in
            // Rgba16ToP010, so the matrix tag (Bt202010) is set by ApplyColorTags
            // independently of VideoSettings.YCbCrMatrix — sdrMatrix is unused here.
            using Bitmap converted = image.Convert(
                BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, hdrTargetColorSpace);
            WriteVideoSample(sinkWriter, streamIndex, converted,
                BitmapColorType.Rgba16161616, ptsHns, durationHns, sdrMatrix);
        }
        else
        {
            // Convert to the SDR target color space matching the transfer/primaries
            // tag we wrote into the output stream. Forcing sRGB here regardless of
            // user selection put BT.709 pixels into an sRGB transfer (slight low-
            // luma drift) before — sdrSourceColorSpace aligns the conversion with
            // the on-the-wire tag.
            using Bitmap converted = image.Convert(
                BitmapColorType.Bgra8888, BitmapAlphaType.Premul, sdrSourceColorSpace);
            WriteVideoSample(sinkWriter, streamIndex, converted,
                BitmapColorType.Bgra8888, ptsHns, durationHns, sdrMatrix);
        }
    }

    private static unsafe void WriteVideoSample(
        IMFSinkWriter sinkWriter, int streamIndex, Bitmap converted,
        BitmapColorType srcKind, long ptsHns, long durationHns,
        PixelFormatConverter.YuvMatrix8 sdrMatrix)
    {
        int width = converted.Width;
        int height = converted.Height;

        // 4:2:0 chroma rows hold ceil(width/2) U/V pairs (= chromaWidth bytes
        // for NV12, 2x for P010). Y rows only need width bytes, but the plane
        // shares dstStride, so it must accommodate the longer chroma row —
        // otherwise odd widths (e.g. 853) write one byte past every UV row.
        bool isP010 = srcKind == BitmapColorType.Rgba16161616;
        int chromaWidth = ((width + 1) / 2) * 2;
        int dstStride = isP010 ? chromaWidth * 2 : chromaWidth;
        int ySize = dstStride * height;
        int uvHeight = (height + 1) / 2;
        int uvSize = dstStride * uvHeight;
        int totalSize = ySize + uvSize;

        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(totalSize);
        buffer.Lock(out IntPtr dstPtr, out _, out _);
        byte* dst = (byte*)dstPtr;
        try
        {
            // MFCreateMemoryBuffer does not document zero-initialization, and at
            // odd widths (e.g. 853) the Y-plane converter writes `width` bytes per
            // row while the shared stride is `chromaWidth` (854). The trailing
            // padding bytes would otherwise carry whatever garbage the allocator
            // returned, which encoder MFTs may read when they walk full rows.
            System.Runtime.InteropServices.NativeMemory.Clear(dst, (nuint)totalSize);

            byte* src = (byte*)converted.Data;
            int srcStride = converted.RowBytes;
            if (isP010)
            {
                PixelFormatConverter.Rgba16ToP010(src, srcStride, dst, dstStride, width, height);
            }
            else
            {
                PixelFormatConverter.BgraToNv12(src, srcStride, dst, dstStride, width, height, sdrMatrix);
            }
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = totalSize;

        using IMFSample sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = ptsHns;
        sample.SampleDuration = durationHns;
        sinkWriter.WriteSample(streamIndex, sample);
    }

    private static async ValueTask WriteAudioFrame(
        IMFSinkWriter sinkWriter, int streamIndex,
        ISampleProvider sampleProvider,
        long startSample, int length, int sampleRate, int channels)
    {
        if (sampleRate <= 0)
        {
            // Unreachable in normal flow: ConfigureAudioStream throws on <= 0, so by
            // the time encodeAudio is true the rate is positive. If a future caller
            // bypasses that guarantee, fail rather than silently drop frames.
            throw new InvalidOperationException(
                $"WriteAudioFrame invoked with non-positive sample rate {sampleRate}.");
        }

        using Pcm<Stereo32BitFloat> floatPcm = await sampleProvider.Sample(startSample, length);
        int numSamples = floatPcm.NumSamples;
        int byteCount = numSamples * channels * 2; // int16

        using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(byteCount);
        buffer.Lock(out IntPtr dstPtr, out _, out _);
        unsafe
        {
            short* dst = (short*)dstPtr;
            try
            {
                ReadOnlySpan<Stereo32BitFloat> src = floatPcm.DataSpan;
                if (channels == 1)
                {
                    // L+R averaged into a single channel keeps both sides audible
                    // without doubling perceived amplitude.
                    for (int i = 0; i < src.Length; i++)
                    {
                        float mono = (src[i].Left + src[i].Right) * 0.5f;
                        dst[i] = FloatToPcm16(mono);
                    }
                }
                else
                {
                    for (int i = 0; i < src.Length; i++)
                    {
                        dst[i * 2 + 0] = FloatToPcm16(src[i].Left);
                        dst[i * 2 + 1] = FloatToPcm16(src[i].Right);
                    }
                }
            }
            finally
            {
                buffer.Unlock();
            }
        }
        buffer.CurrentLength = byteCount;

        long ptsHns = startSample * HnsPerSecond / sampleRate;
        long durationHns = (long)numSamples * HnsPerSecond / sampleRate;

        using IMFSample sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = ptsHns;
        sample.SampleDuration = durationHns;
        sinkWriter.WriteSample(streamIndex, sample);
    }

    private static short FloatToPcm16(float sample)
    {
        // Use 32767 (not 32768) for the positive side to avoid clipping +1.0 into
        // a wraparound. Symmetric clamp keeps the DC bias out of the output.
        float scaled = sample * 32767f;
        if (scaled > 32767f) scaled = 32767f;
        else if (scaled < -32768f) scaled = -32768f;
        return (short)scaled;
    }

    private static ulong PackUint64(uint hi, uint lo) => ((ulong)hi << 32) | lo;

    // MapXxxToMF: encoder output tag domain (Vortice VideoXxx enum, used as
    // MF_MT_* attribute values). MapXxxForHelper: input to MFColorSpaceHelper
    // (same enum, but Default already resolved into a concrete HDR transfer/
    // primaries so Skia gets a fully-specified color space).
    internal static VideoTransferFunction MapTransferToMF(
        MFVideoEncoderSettings.ColorTransferCharacteristic t, bool isHdr)
    {
        // HDR must have a valid transfer tag — default to PQ (HDR10) when the user
        // left the field unset. SDR leaves it Unknown (decoder default kicks in).
        if (t == MFVideoEncoderSettings.ColorTransferCharacteristic.Default)
            return isHdr ? VideoTransferFunction.Func2084 : VideoTransferFunction.FuncUnknown;

        return t switch
        {
            MFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => VideoTransferFunction.FuncSRGB,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Linear => VideoTransferFunction.Func10,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => VideoTransferFunction.Func709,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Pq => VideoTransferFunction.Func2084,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => VideoTransferFunction.FuncHlg,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => VideoTransferFunction.Func240m,
            _ => VideoTransferFunction.FuncUnknown,
        };
    }

    internal static VideoPrimaries MapPrimariesToMF(
        MFVideoEncoderSettings.ColorPrimariesType p, bool isHdr)
    {
        if (p == MFVideoEncoderSettings.ColorPrimariesType.Default)
            return isHdr ? VideoPrimaries.Bt2020 : VideoPrimaries.Unknown;

        return p switch
        {
            MFVideoEncoderSettings.ColorPrimariesType.Bt709 => VideoPrimaries.Bt709,
            MFVideoEncoderSettings.ColorPrimariesType.Rec2020 => VideoPrimaries.Bt2020,
            MFVideoEncoderSettings.ColorPrimariesType.Dcip3 => VideoPrimaries.DciP3,
            MFVideoEncoderSettings.ColorPrimariesType.Smpte170M => VideoPrimaries.Smpte170m,
            _ => VideoPrimaries.Unknown,
        };
    }

    // Picks the 8-bit NV12 conversion matrix matching the tag MapMatrixToMF will
    // write. Default falls back to BT.709 (typical SDR HD); the tag side writes
    // Unknown for Default. Most modern players resolve missing matrix tags to
    // BT.709, but at SD resolutions (480p / 576p) decoders frequently apply
    // BT.601 instead — so a Default + SD-sized output may decode with a slight
    // hue shift. Users encoding SD content should select Bt601 explicitly to
    // pin the matrix tag and avoid this ambiguity.
    internal static PixelFormatConverter.YuvMatrix8 ResolveSdrYuvMatrix(
        MFVideoEncoderSettings.YCbCrMatrixType m)
    {
        return m switch
        {
            MFVideoEncoderSettings.YCbCrMatrixType.Bt601 => PixelFormatConverter.YuvMatrix8.Bt601,
            MFVideoEncoderSettings.YCbCrMatrixType.Rec2020 => PixelFormatConverter.YuvMatrix8.Bt2020,
            MFVideoEncoderSettings.YCbCrMatrixType.Smpte240M => PixelFormatConverter.YuvMatrix8.Smpte240M,
            _ => PixelFormatConverter.YuvMatrix8.Bt709,
        };
    }

    internal static VideoTransferMatrix MapMatrixToMF(
        MFVideoEncoderSettings.YCbCrMatrixType m, bool isHdr)
    {
        if (m == MFVideoEncoderSettings.YCbCrMatrixType.Default)
            return isHdr ? VideoTransferMatrix.Bt202010 : VideoTransferMatrix.Unknown;

        return m switch
        {
            MFVideoEncoderSettings.YCbCrMatrixType.Bt709 => VideoTransferMatrix.Bt709,
            MFVideoEncoderSettings.YCbCrMatrixType.Bt601 => VideoTransferMatrix.Bt601,
            MFVideoEncoderSettings.YCbCrMatrixType.Rec2020 => VideoTransferMatrix.Bt202010,
            MFVideoEncoderSettings.YCbCrMatrixType.Smpte240M => VideoTransferMatrix.Smpte240m,
            _ => VideoTransferMatrix.Unknown,
        };
    }

    // Resolve a settings enum to a Vortice transfer/primaries value with the
    // Default branch turned into a concrete value, so MFColorSpaceHelper does
    // not have to second-guess. `isHdr` decides which side's default applies:
    // HDR → PQ + Rec.2020, SDR → sRGB + Bt.709. Previously the unified default
    // mapped Default→PQ for SDR too, which produced PQ-tagged sRGB pixels.
    internal static VideoTransferFunction MapTransferForHelper(
        MFVideoEncoderSettings.ColorTransferCharacteristic t, bool isHdr)
    {
        return t switch
        {
            MFVideoEncoderSettings.ColorTransferCharacteristic.Pq => VideoTransferFunction.Func2084,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => VideoTransferFunction.FuncHlg,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => VideoTransferFunction.Func709,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => VideoTransferFunction.FuncSRGB,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Linear => VideoTransferFunction.Func10,
            MFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => VideoTransferFunction.Func240m,
            _ => isHdr ? VideoTransferFunction.Func2084 : VideoTransferFunction.FuncSRGB,
        };
    }

    internal static VideoPrimaries MapPrimariesForHelper(
        MFVideoEncoderSettings.ColorPrimariesType p, bool isHdr)
    {
        return p switch
        {
            MFVideoEncoderSettings.ColorPrimariesType.Bt709 => VideoPrimaries.Bt709,
            MFVideoEncoderSettings.ColorPrimariesType.Rec2020 => VideoPrimaries.Bt2020,
            MFVideoEncoderSettings.ColorPrimariesType.Dcip3 => VideoPrimaries.DciP3,
            MFVideoEncoderSettings.ColorPrimariesType.Smpte170M => VideoPrimaries.Smpte170m,
            _ => isHdr ? VideoPrimaries.Bt2020 : VideoPrimaries.Bt709,
        };
    }
}
