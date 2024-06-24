namespace Beutl.Extensions.AVFoundation.Decoding;

public record AVFSampleCacheOptions(
    int MaxVideoBufferSize = 4, // あまり大きな値を設定するとReadSampleで停止する
    int MaxAudioBufferSize = 20);
