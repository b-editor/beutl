using System.Net.WebSockets;
using System.Text.Json;
using Beutl.Api;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

internal sealed partial class MobilePreviewServer
{
    private (string Id, string Address)[]? _relayClients;
    private Task? _relayTask;
    internal event EventHandler? RelayDisconnected;

    internal static async Task<MobilePreviewServer> StartRemoteAsync(PreviewSessionResponse session,
        CancellationToken cancellationToken = default)
    {
        var server = new MobilePreviewServer(null, TimeProvider.System)
        {
            Endpoint = session.ViewerUrl,
            _pairingCode = session.PairingCode,
            _codeExpires = session.CodeExpires,
            _relayClients = []
        };
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + session.PublisherToken);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        try
        {
            await socket.ConnectAsync(session.PublisherUrl, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var client = new Client(socket, "relay");
            server._clients.Add("relay", client);
            server._encoderTask = Task.Run(server.EncodeFramesAsync);
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
                _clients.Clear();
            }
            ClientsChanged?.Invoke(this, EventArgs.Empty);
            if (!_stop.IsCancellationRequested) RelayDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SendRelayControl(object control)
    {
        if (_relayClients is not null && _clients.TryGetValue("relay", out var client))
            client.Controls.Writer.TryWrite(JsonSerializer.Serialize(control));
    }

    private bool HandleRelayMessage(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            if (!json.RootElement.TryGetProperty("clients", out var clients)) return false;
            var snapshot = clients.EnumerateArray().Select(c => (
                c.GetProperty("id").GetString()!, c.GetProperty("address").GetString()!)).ToArray();
            lock (_gate) _relayClients = snapshot;
            ClientsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (JsonException) { return false; }
    }
}
