namespace Beutl.FFmpegIpc.Protocol.Messages;

public sealed class HandshakeMessage
{
    /// <summary>
    /// IPCプロトコルバージョン。ホストとWorkerで一致しない場合はエラーとする。
    /// </summary>
    public int ProtocolVersion { get; set; } = ProtocolConstants.CurrentVersion;
}

public static class ProtocolConstants
{
    public const int CurrentVersion = 1;
}
