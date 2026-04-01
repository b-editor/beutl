namespace Beutl.FFmpegIpc.Protocol.Messages;

public sealed class EncodeStartRequest
{
    public string OutputFile { get; set; } = "";

    // Frame provider metadata
    public long FrameCount { get; set; }
    public long FrameRateNum { get; set; }
    public long FrameRateDen { get; set; }

    // Sample provider metadata
    public long SampleCount { get; set; }
    public long ProviderSampleRate { get; set; }

    // Video settings
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int DestWidth { get; set; }
    public int DestHeight { get; set; }
    public int VideoBitrate { get; set; }
    public int KeyframeRate { get; set; }
    public int PixelFormat { get; set; } // AVPixelFormat as int
    public string VideoCodecName { get; set; } = "Default";
    public FFColorPrimaries ColorPrimaries { get; set; }
    public FFColorTransfer ColorTrc { get; set; }
    public FFColorSpace ColorSpace { get; set; }
    public FFColorRange ColorRange { get; set; }
    public Dictionary<string, string> VideoOptions { get; set; } = [];
    public bool IsHdr { get; set; }

    // Audio settings
    public int AudioSampleRate { get; set; }
    public int AudioChannels { get; set; }
    public int AudioBitrate { get; set; }
    public int AudioFormat { get; set; } // AVSampleFormat as int
    public string AudioCodecName { get; set; } = "Default";

    // Global settings
    public int ThreadCount { get; set; } = -1;
    public int Acceleration { get; set; }
}

public sealed class EncodeStartAckMessage
{
    public string VideoSharedMemoryName { get; set; } = "";
    public string AudioSharedMemoryName { get; set; } = "";
    public long VideoBufferSize { get; set; }
    public long AudioBufferSize { get; set; }
}

public sealed class RequestFrameMessage
{
    public long FrameIndex { get; set; }
    public bool IsHdr { get; set; }
}

public sealed class ProvideFrameMessage
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int BytesPerPixel { get; set; }
    public int DataLength { get; set; }
    public bool Premul { get; set; }
}

public sealed class RequestSampleMessage
{
    public long Offset { get; set; }
    public long Length { get; set; }
}

public sealed class ProvideSampleMessage
{
    public int NumSamples { get; set; }
    public int DataLength { get; set; }
}

public sealed class EncodeProgressMessage
{
    public long VideoFramesDone { get; set; }
    public long AudioSamplesDone { get; set; }
}

public sealed class EncodeCompleteMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
