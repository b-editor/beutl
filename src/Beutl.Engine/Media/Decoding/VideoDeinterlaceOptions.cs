namespace Beutl.Media.Decoding;

public enum DeinterlaceMode
{
    Off = 0,
    Auto = 1,
    On = 2,
}

public enum DeinterlaceFieldOrder
{
    Auto = 0,
    TopFieldFirst = 1,
    BottomFieldFirst = 2,
}

public enum DeinterlaceMethod
{
    Bwdif = 0,
    Yadif = 1,
}

public enum DeinterlaceFrameRateMode
{
    Frame = 0,
    Field = 1,
}

public enum VideoFieldOrder
{
    Unknown = 0,
    Progressive = 1,
    TopFieldFirst = 2,
    BottomFieldFirst = 3,
    Mixed = 4,
}

public enum DeinterlaceApplicationStatus
{
    Off = 0,
    NotNeededProgressive = 1,
    Applied = 2,
    UnsupportedByBackend = 3,
}

public readonly record struct VideoDeinterlaceOptions(
    DeinterlaceMode Mode,
    DeinterlaceFieldOrder FieldOrder,
    DeinterlaceMethod Method,
    DeinterlaceFrameRateMode FrameRateMode)
{
    public static VideoDeinterlaceOptions Default => default;

    public VideoDeinterlaceOptions Normalize() => Mode == DeinterlaceMode.Off ? Default : this;

    public DeinterlaceApplicationStatus GetFallbackStatus(VideoFieldOrder detectedFieldOrder) => Mode switch
    {
        DeinterlaceMode.Off => DeinterlaceApplicationStatus.Off,
        DeinterlaceMode.Auto when detectedFieldOrder == VideoFieldOrder.Progressive =>
            DeinterlaceApplicationStatus.NotNeededProgressive,
        _ => DeinterlaceApplicationStatus.UnsupportedByBackend,
    };
}
