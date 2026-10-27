using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Beutl.Editor.Models;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private readonly Queue<byte[]> _recentAudio = new();
    private long _audioPlaybackId;

    internal void PublishAudio(AudioFrameSnapshot audio)
    {
        if (audio.PlaybackId == 0) return;
        byte[] packet = PreviewMediaPacket.Audio(audio);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            if (_audioPlaybackId != audio.PlaybackId) _recentAudio.Clear();
            _audioPlaybackId = audio.PlaybackId;
            _recentAudio.Enqueue(packet);
            while (_recentAudio.Count > 2) _recentAudio.Dequeue();
            foreach (var client in _clients.Values)
                if (client.AudioEnabled) client.Audio.Writer.TryWrite(packet);
        }
    }

    internal void PublishStopped()
    {
        lock (_gate)
        {
            _recentAudio.Clear();
            foreach (var client in _clients.Values)
            {
                while (client.Audio.Reader.TryRead(out _)) { }
                client.Controls.Writer.TryWrite(JsonSerializer.Serialize(new { playing = false, playbackId = _audioPlaybackId }));
            }
        }
    }

    private async Task RunClientAsync(Client client, CancellationTokenSource cancellation)
    {
        using var sendGate = new SemaphoreSlim(1);
        using var videoReady = new SemaphoreSlim(1, 1);
        using var audioReady = new SemaphoreSlim(1, 1);
        var token = cancellation.Token;
        Task video = SendMediaAsync(client.Frames.Reader, videoReady);
        Task audio = SendMediaAsync(client.Audio.Reader, audioReady);
        Task controls = SendControlsAsync();
        Task receive = ReceiveAsync();
        try { await Task.WhenAny(video, audio, controls, receive); }
        finally
        {
            cancellation.Cancel();
            client.Socket.Abort();
            try { await Task.WhenAll(video, audio, controls, receive); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        }

        async Task SendMediaAsync(ChannelReader<byte[]> reader, SemaphoreSlim ready)
        {
            while (!token.IsCancellationRequested)
            {
                await ready.WaitAsync(token);
                byte[] packet = await reader.ReadAsync(token);
                await SendAsync(packet, WebSocketMessageType.Binary);
            }
        }

        async Task SendControlsAsync()
        {
            await foreach (string control in client.Controls.Reader.ReadAllAsync(token))
                await SendAsync(Encoding.UTF8.GetBytes(control), WebSocketMessageType.Text);
        }

        async Task SendAsync(byte[] data, WebSocketMessageType type)
        {
            await sendGate.WaitAsync(token);
            try { await client.Socket.SendAsync(data.AsMemory(), type, true, token); }
            finally { sendGate.Release(); }
        }

        async Task ReceiveAsync()
        {
            byte[] buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                int count = 0;
                ValueWebSocketReceiveResult message;
                do
                {
                    message = await client.Socket.ReceiveAsync(buffer.AsMemory(count), token);
                    if (message.MessageType != WebSocketMessageType.Text) return;
                    count += message.Count;
                    if (count == buffer.Length && !message.EndOfMessage) return;
                } while (!message.EndOfMessage);
                string command = Encoding.UTF8.GetString(buffer, 0, count);
                switch (command)
                {
                    case "ready":
                        if (videoReady.CurrentCount == 0) videoReady.Release();
                        break;
                    case "audio-ready":
                        if (audioReady.CurrentCount == 0) audioReady.Release();
                        break;
                    case "audio-on":
                    case "audio-off":
                        lock (_gate)
                        {
                            client.AudioEnabled = command == "audio-on";
                            while (client.Audio.Reader.TryRead(out _)) { }
                            if (client.AudioEnabled)
                                foreach (var packet in _recentAudio) client.Audio.Writer.TryWrite(packet);
                        }
                        break;
                    default:
                        if (_relayClients is null || !HandleRelayMessage(command)) return;
                        break;
                }
            }
        }
    }

    private sealed record Client(WebSocket Socket, string Address)
    {
        internal bool AudioEnabled { get; set; }
        internal Channel<byte[]> Frames { get; } = Bounded<byte[]>(1);
        internal Channel<byte[]> Audio { get; } = Bounded<byte[]>(2);
        internal Channel<string> Controls { get; } = Bounded<string>(16);
    }

    private static Channel<T> Bounded<T>(int capacity) => Channel.CreateBounded<T>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest });
}
