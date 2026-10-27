using System.Net.WebSockets;
using System.Text.Json;
using Beutl.Api;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private (string Id, string Address)[] _relayClients = [];
    private Client? _relay;
    private Task _relayTask = Task.CompletedTask;
    internal event EventHandler? RelayDisconnected;

    internal static async Task<MobilePreviewServer> StartAsync(PreviewSessionResponse session,
        CancellationToken cancellationToken = default)
    {
        var server = new MobilePreviewServer()
        {
            Endpoint = session.ViewerUrl,
            _pairingCode = session.PairingCode,
            _codeExpires = session.CodeExpires,
            _relayClients = [],
            _iceServers = session.IceServers ?? []
        };
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + session.PublisherToken);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        try
        {
            await socket.ConnectAsync(session.PublisherUrl, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var client = new Client(socket);
            server._relay = client;
            server._encoderTask = Task.Run(server.EncodeFramesAsync);
            server._audioTask = Task.Run(server.EncodeAudioAsync);
            server._relayTask = server.RunRelayAsync(client);
            return server;
        }
        catch
        {
            socket.Dispose();
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunRelayAsync(Client client)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        try { await RunClientAsync(client, cancellation).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Log.CreateLogger<MobilePreviewServer>().LogWarning(ex, "Mobile preview relay disconnected.");
        }
        finally
        {
            client.Socket.Dispose();
            lock (_gate)
            {
                _relayClients = [];
                _relay = null;
                _paired.Clear();
            }
            ClientsChanged?.Invoke(this, EventArgs.Empty);
            if (!_stop.IsCancellationRequested) RelayDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SendRelayControl(object control, string? playbackId = null)
    {
        if (_relay is { } client)
            client.Controls.Enqueue(JsonSerializer.Serialize(control), playbackId);
    }

    private async Task HandleRelayMessageAsync(JsonElement message, CancellationToken ct)
    {
        if (message.TryGetProperty("clients", out var clients))
        {
            var snapshot = clients.EnumerateArray().Select(c => (
                c.GetProperty("id").GetString()!, c.GetProperty("address").GetString()!)).ToArray();
            string[] removed;
            lock (_gate)
            {
                removed = _peers.Keys.Except(snapshot.Select(c => c.Item1)).ToArray();
                _relayClients = snapshot;
                if (snapshot.Length == 0)
                {
                    _latest?.Dispose();
                    _latest = null;
                }
                _paired.Clear();
                foreach (var device in message.TryGetProperty("paired", out var paired) ? paired.EnumerateArray() : clients.EnumerateArray())
                    _paired[device.GetProperty("id").GetString()!] = device.GetProperty("address").GetString()!;
                foreach (var client in snapshot) SendToViewer(client.Item1, _playbackState, playback: true);
            }
            foreach (string id in removed) await RemovePeerAsync(id).ConfigureAwait(false);
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (message.TryGetProperty("signal", out var signal))
            await HandleSignalAsync(message.GetProperty("id").GetString()!, signal, ct).ConfigureAwait(false);
    }
}
