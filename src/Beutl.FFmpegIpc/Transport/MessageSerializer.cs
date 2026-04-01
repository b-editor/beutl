using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using Beutl.FFmpegIpc.Protocol;

namespace Beutl.FFmpegIpc.Transport;

/// <summary>
/// 長さプレフィックス付きJSON over Stream のメッセージシリアライザ。
/// Format: [4 bytes: length (int32 LE)] [N bytes: UTF-8 JSON]
/// </summary>
public static class MessageSerializer
{
    private const int LengthPrefixSize = 4;
    private const int MaxMessageSize = 64 * 1024 * 1024; // 64MB

    public static async ValueTask WriteMessageAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        // JSONをArrayPoolバッファにシリアライズ
        byte[] jsonBuf = JsonSerializer.SerializeToUtf8Bytes(message, IpcJsonContext.Default.Options);
        int totalLength = LengthPrefixSize + jsonBuf.Length;

        // 長さプレフィックス + JSONを単一バッファに結合して1回のWriteで送信
        byte[] sendBuf = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(sendBuf, jsonBuf.Length);
            jsonBuf.CopyTo(sendBuf, LengthPrefixSize);

            await stream.WriteAsync(sendBuf.AsMemory(0, totalLength), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuf);
        }
    }

    public static IpcMessage? ReadMessage(Stream stream)
    {
        Span<byte> lengthBuf = stackalloc byte[LengthPrefixSize];
        int bytesRead = ReadExactly(stream, lengthBuf);
        if (bytesRead < LengthPrefixSize)
            return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
        if (length <= 0 || length > MaxMessageSize)
            throw new InvalidOperationException($"Invalid message length: {length}");

        byte[] jsonBuf = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            bytesRead = ReadExactly(stream, jsonBuf.AsSpan(0, length));
            if (bytesRead < length)
                return null;

            return JsonSerializer.Deserialize<IpcMessage>(
                jsonBuf.AsSpan(0, length), IpcJsonContext.Default.Options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(jsonBuf);
        }
    }

    public static async ValueTask<IpcMessage?> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] lengthBuf = ArrayPool<byte>.Shared.Rent(LengthPrefixSize);
        try
        {
            int bytesRead = await ReadExactlyAsync(stream, lengthBuf.AsMemory(0, LengthPrefixSize), ct).ConfigureAwait(false);
            if (bytesRead < LengthPrefixSize)
                return null;

            int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf.AsSpan(0, LengthPrefixSize));
            if (length <= 0 || length > MaxMessageSize)
                throw new InvalidOperationException($"Invalid message length: {length}");

            byte[] jsonBuf = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                bytesRead = await ReadExactlyAsync(stream, jsonBuf.AsMemory(0, length), ct).ConfigureAwait(false);
                if (bytesRead < length)
                    return null;

                return JsonSerializer.Deserialize<IpcMessage>(
                    jsonBuf.AsSpan(0, length), IpcJsonContext.Default.Options);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(jsonBuf);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuf);
        }
    }

    private static int ReadExactly(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    private static async ValueTask<int> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..], ct).ConfigureAwait(false);
            if (read == 0)
                return totalRead;
            totalRead += read;
        }
        return totalRead;
    }
}
