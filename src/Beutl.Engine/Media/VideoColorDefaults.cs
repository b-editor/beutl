namespace Beutl.Media;

/// <summary>The YCbCr matrix an encoded video stream declares.</summary>
public enum VideoColorMatrix
{
    Unknown = 0,
    /// <summary>The stream stores RGB (or GBR) samples without a YCbCr matrix.</summary>
    Identity = 1,
    Bt709 = 2,
    Bt601 = 3,
    Bt2020 = 4,
}

/// <summary>The sample range an encoded video stream declares.</summary>
public enum VideoColorRange
{
    Unknown = 0,
    Limited = 1,
    Full = 2,
}

/// <summary>What an encoder writes, which decides the color tags it falls back to.</summary>
public enum VideoColorTarget
{
    /// <summary>Limited-range YCbCr, as delivery video uses.</summary>
    YCbCr,
    /// <summary>Full-range YCbCr outside the JPEG family, such as yuvj420p H.264.</summary>
    FullRangeYCbCr,
    /// <summary>JPEG-family YCbCr, which follows JFIF: sRGB-like code values and the BT.601 matrix at full range.</summary>
    Jpeg,
    /// <summary>RGB samples.</summary>
    Rgb,
}

/// <summary>The color tags of an encoded video stream.</summary>
public readonly record struct VideoColorTags(
    BitmapColorTransfer Transfer,
    BitmapColorPrimaries Primaries,
    VideoColorMatrix Matrix,
    VideoColorRange Range);

/// <summary>
/// The tags every encoder writes for the color settings a user leaves unspecified, so FFmpeg, AVFoundation and
/// Media Foundation exports agree. An encoder keeps the values that were set and translates these to its own tags.
/// </summary>
public static class VideoColorDefaults
{
    /// <summary>
    /// Returns the tags for unspecified settings. Where the output cannot record tags (<paramref name="recordsColor"/>
    /// is false), SDR stays untagged, because readers then assume their untagged defaults.
    /// </summary>
    public static VideoColorTags Get(VideoColorTarget target, bool hdr, bool recordsColor)
    {
        if (!hdr && !recordsColor)
            return default;

        bool rgb = target == VideoColorTarget.Rgb;
        bool jpeg = target == VideoColorTarget.Jpeg;
        return new VideoColorTags(
            rgb || jpeg ? BitmapColorTransfer.Srgb : BitmapColorTransfer.Bt709,
            hdr ? BitmapColorPrimaries.Rec2020 : BitmapColorPrimaries.Bt709,
            rgb ? VideoColorMatrix.Identity
                : hdr ? VideoColorMatrix.Bt2020
                : jpeg ? VideoColorMatrix.Bt601 : VideoColorMatrix.Bt709,
            target == VideoColorTarget.YCbCr ? VideoColorRange.Limited : VideoColorRange.Full);
    }
}
