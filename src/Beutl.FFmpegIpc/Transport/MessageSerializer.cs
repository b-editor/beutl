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
    public static async ValueTask WriteMessageAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, IpcJsonContext.Default.Options);
        byte[] lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, json.Length);

        await stream.WriteAsync(lengthPrefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(json, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static IpcMessage? ReadMessage(Stream stream)
    {
        byte[] lengthBuf = new byte[4];
        int bytesRead = ReadExactly(stream, lengthBuf);
        if (bytesRead < 4)
            return null; // Connection closed

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
        if (length <= 0 || length > 64 * 1024 * 1024) // 64MB max
            throw new InvalidOperationException($"Invalid message length: {length}");

        byte[] jsonBuf = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            bytesRead = ReadExactly(stream, jsonBuf.AsMemory(0, length));
            if (bytesRead < length)
                return null; // Connection closed

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
        byte[] lengthBuf = new byte[4];
        int bytesRead = await ReadExactlyAsync(stream, lengthBuf, ct).ConfigureAwait(false);
        if (bytesRead < 4)
            return null; // Connection closed

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);
        if (length <= 0 || length > 64 * 1024 * 1024) // 64MB max
            throw new InvalidOperationException($"Invalid message length: {length}");

        byte[] jsonBuf = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            bytesRead = await ReadExactlyAsync(stream, jsonBuf.AsMemory(0, length), ct).ConfigureAwait(false);
            if (bytesRead < length)
                return null; // Connection closed

            return JsonSerializer.Deserialize<IpcMessage>(
                jsonBuf.AsSpan(0, length), IpcJsonContext.Default.Options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(jsonBuf);
        }
    }

    private static int ReadExactly(Stream stream, Memory<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..].Span);
            if (read == 0)
                return totalRead; // EOF
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
                return totalRead; // EOF
            totalRead += read;
        }
        return totalRead;
    }
}
