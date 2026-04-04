namespace Beutl.FFmpegIpc.Protocol;

public enum MessageType
{
    // ライフサイクル
    Handshake = 0,
    HandshakeAck = 1,
    Shutdown = 3,

    // デコード
    OpenFile = 10,
    OpenFileResult = 11,
    ReadVideo = 12,
    ReadVideoResult = 13,
    ReadAudio = 14,
    ReadAudioResult = 15,
    CloseReader = 16,
    CloseReaderResult = 17,
    UpdateDecoderSettings = 18,
    UpdateDecoderSettingsResult = 19,

    // エンコード
    StartEncode = 30,
    StartEncodeAck = 31,
    RequestFrame = 32,
    ProvideFrame = 33,
    RequestSample = 34,
    ProvideSample = 35,
    EncodeProgress = 36,
    EncodeComplete = 37,
    CancelEncode = 38,

    // コーデック/フォーマット照会
    QueryCodecs = 40,
    QueryCodecsResult = 41,
    QueryPixelFormats = 42,
    QueryPixelFormatsResult = 43,
    QuerySampleRates = 44,
    QuerySampleRatesResult = 45,
    QueryAudioFormats = 46,
    QueryAudioFormatsResult = 47,
    QueryDefaultCodec = 48,
    QueryDefaultCodecResult = 49,

    // エラー
    Error = 99,
}
