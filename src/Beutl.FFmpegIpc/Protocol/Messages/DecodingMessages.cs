namespace Beutl.FFmpegIpc.Protocol.Messages;

public sealed class OpenFileRequest
{
    public string FilePath { get; set; } = "";
    public int StreamsToLoad { get; set; } // MediaMode flags
    public int ThreadCount { get; set; } = -1;
    public int Acceleration { get; set; }
}

public sealed class OpenFileResponse
{
    public int ReaderId { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }

    // 共有メモリ名 (Worker側で作成済み)
    public string? VideoSharedMemoryName { get; set; }
    public string? AudioSharedMemoryName { get; set; }

    // Video stream info
    public string? VideoCodecName { get; set; }
    public long VideoNumFrames { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public long FrameRateNum { get; set; }
    public long FrameRateDen { get; set; }
    public long DurationNum { get; set; }
    public long DurationDen { get; set; }

    // Audio stream info
    public string? AudioCodecName { get; set; }
    public long AudioDurationNum { get; set; }
    public long AudioDurationDen { get; set; }
    public int AudioSampleRate { get; set; }
    public int AudioNumChannels { get; set; }
    public uint AudioSpeakerMask { get; set; } // 0 means discrete channels without speaker positions

    // リングバッファ情報
    public int VideoRingBufferSlotCount { get; set; } // 0 when the file has no video
    public long VideoRingBufferSlotSize { get; set; }
}

public sealed class ReadVideoRequest
{
    public int ReaderId { get; set; }
    public int Frame { get; set; }
}

public sealed class ReadVideoResponse
{
    public bool Success { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int BytesPerPixel { get; set; }
    // 共有メモリ名。リサイズにより変更された場合に設定される
    public string? SharedMemoryName { get; set; }
    // Color space can change per frame in some cases
    public float[]? TransferFn { get; set; }
    public float[]? ToXyzD50 { get; set; }
    // リングバッファ: フレームデータの読み取り位置
    public long SlotDataOffset { get; set; }
    // リサイズ時に更新されたリングバッファのサイズ情報
    public long? RingBufferSlotSize { get; set; }
    public int? RingBufferSlotCount { get; set; }
}

public sealed class ReadAudioRequest
{
    public int ReaderId { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
}

public sealed class ReadAudioResponse
{
    public bool Success { get; set; }
    public int SampleRate { get; set; }
    public int NumChannels { get; set; }
    public int NumSamples { get; set; }
    public int DataLength { get; set; }
}

public sealed class CloseReaderRequest
{
    public int ReaderId { get; set; }
}
