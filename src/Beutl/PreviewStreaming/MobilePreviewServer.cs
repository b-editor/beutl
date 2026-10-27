using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.PreviewStreaming;

// A session-scoped, read-only server. It has no reference to project files or editing APIs.
internal sealed partial class MobilePreviewServer : IAsyncDisposable
{
    private const int MaxClients = 4;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _clock;
    private readonly WebApplication? _app;
    private readonly HashSet<string> _paired = [];
    private readonly Dictionary<string, Client> _clients = [];
    private readonly string _cookieName = "beutl-preview-" + Guid.NewGuid().ToString("N");
    private string _pairingCode = "";
    private DateTimeOffset _codeExpires;
    private DateTimeOffset _attemptWindow;
    private int _pairAttempts;
    private Task? _disposeTask;
    private Task _encoderTask = Task.CompletedTask;

    private MobilePreviewServer(IPAddress? address, TimeProvider clock)
    {
        _clock = clock;
        RenewPairingCode();
        if (address is null) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(MobilePreviewServer).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(address, 0);
            options.Limits.MaxRequestBodySize = 64;
            options.Limits.MaxRequestHeadersTotalSize = 8192;
        });
        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15),
            KeepAliveTimeout = TimeSpan.FromSeconds(15)
        });
        _app.Run(HandleRequestAsync);
    }

    internal Uri Endpoint { get; private set; } = null!;
    internal string PairingCode { get { lock (_gate) return _pairingCode; } }
    internal DateTimeOffset CodeExpires { get { lock (_gate) return _codeExpires; } }
    internal event EventHandler? ClientsChanged;

    internal static async Task<MobilePreviewServer> StartAsync(IPAddress address,
        CancellationToken cancellationToken = default, TimeProvider? clock = null)
    {
        var server = new MobilePreviewServer(address, clock ?? TimeProvider.System);
        try
        {
            await server._app!.StartAsync(cancellationToken).ConfigureAwait(false);
            server.Endpoint = new Uri(server._app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            cancellationToken.ThrowIfCancellationRequested();
            server._encoderTask = Task.Run(server.EncodeFramesAsync);
            server._audioTask = Task.Run(server.EncodeAudioAsync);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void RenewPairingCode()
    {
        lock (_gate)
        {
            _pairingCode = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            _codeExpires = _clock.GetUtcNow().AddMinutes(10);
            _attemptWindow = _clock.GetUtcNow();
            _pairAttempts = 0;
            SendRelayControl(new { type = "pairing", code = _pairingCode, expires = _codeExpires.ToUnixTimeMilliseconds() });
        }
    }

    internal (string Id, string Address)[] GetClients()
    {
        lock (_gate)
            return _relayClients ?? _clients.Select(pair => (pair.Key, pair.Value.Address)).ToArray();
    }

    internal void Disconnect(string id)
    {
        lock (_gate)
        {
            SendRelayControl(new { type = "disconnect", id });
            _paired.Remove(id);
            if (_clients.TryGetValue(id, out var client)) client.Socket.Abort();
        }
    }

    internal void DisconnectAll()
    {
        lock (_gate)
        {
            SendRelayControl(new { type = "disconnectAll" });
            _paired.Clear();
            if (_relayClients is null)
                foreach (var client in _clients.Values) client.Socket.Abort();
        }
        RenewPairingCode();
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        if (Endpoint is null || _stop.IsCancellationRequested)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        // Do not accept arbitrary Host names, forwarded hosts, or cross-origin pairing / sockets.
        if (!string.Equals(request.Host.Value, Endpoint.Authority, StringComparison.OrdinalIgnoreCase)
            || (request.Headers.Origin.Count > 0 && request.Headers.Origin.ToString() != Endpoint.GetLeftPart(UriPartial.Authority)))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; "
            + $"img-src 'self'; media-src 'self' blob:; connect-src 'self' ws://{Endpoint.Authority}; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";

        string? resource = (request.Method, request.Path.Value) switch
        {
            ("GET", "/") => "index.html",
            ("GET", "/viewer.js") => "viewer.js",
            ("GET", "/preview-connection.js") => "preview-connection.js",
            ("GET", "/theme.css") => "theme.css",
            ("GET", "/logo.svg") => "logo.svg",
            ("GET", "/viewer.css") => "viewer.css",
            _ => null
        };
        if (resource is not null)
        {
            response.ContentType = resource.EndsWith(".html") ? "text/html; charset=utf-8"
                : resource.EndsWith(".js") ? "text/javascript; charset=utf-8"
                : resource.EndsWith(".svg") ? "image/svg+xml" : "text/css; charset=utf-8";
            await using Stream stream = typeof(MobilePreviewServer).Assembly
                .GetManifestResourceStream("Beutl.PreviewStreaming.Client." + resource)!;
            await stream.CopyToAsync(response.Body, context.RequestAborted);
            return;
        }
        if (request.Method == "POST" && request.Path == "/api/pair")
        {
            if (request.Headers.Origin.ToString() != Endpoint.GetLeftPart(UriPartial.Authority))
            {
                response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            string code = await reader.ReadToEndAsync(context.RequestAborted);
            Pair(context, code);
            return;
        }
        string? token = request.Cookies[_cookieName];
        bool authorized;
        lock (_gate) authorized = token is not null && _paired.Contains(token);
        if (request.Method == "GET" && request.Path == "/api/session")
        {
            response.StatusCode = authorized ? StatusCodes.Status204NoContent : StatusCodes.Status401Unauthorized;
            return;
        }
        if (request.Path != "/stream" || request.Method != "GET")
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!authorized)
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (!context.WebSockets.IsWebSocketRequest
            || request.Headers.Origin.ToString() != Endpoint.GetLeftPart(UriPartial.Authority))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await StreamAsync(context, token!);
    }

    private void Pair(HttpContext context, string code)
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            if (now - _attemptWindow >= TimeSpan.FromMinutes(1))
            {
                _attemptWindow = now;
                _pairAttempts = 0;
            }
            if (_pairAttempts++ >= 10)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
            if (now >= _codeExpires || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(_pairingCode)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            string? existing = context.Request.Cookies[_cookieName];
            if (existing is not null && _paired.Contains(existing))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            if (_paired.Count >= MaxClients)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _paired.Add(token);
            context.Response.Cookies.Append(_cookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true
            });
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }
    }

    private async Task StreamAsync(HttpContext context, string token)
    {
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new Client(socket, context.Connection.RemoteIpAddress?.ToString() ?? "");
        lock (_gate)
        {
            if (!_paired.Contains(token) || _stop.IsCancellationRequested) return;
            if (_clients.TryGetValue(token, out var previous)) previous.Socket.Abort();
            _clients[token] = client;
        }
        ClientsChanged?.Invoke(this, EventArgs.Empty);
        client.Controls.Writer.TryWrite(System.Text.Json.JsonSerializer.Serialize(new { type = "config", iceServers = _iceServers }));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, context.RequestAborted);
        try
        {
            await RunClientAsync(client, cancellation, token);
        }
        finally
        {
            lock (_gate)
            {
                if (_clients.TryGetValue(token, out var current) && current == client)
                    _clients.Remove(token);
                if (_clients.Count == 0)
                {
                    _latest?.Dispose();
                    _latest = null;
                }
            }
            await RemovePeerAsync(token, client);
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _stop.Cancel();
        DisconnectAll();
        _latest?.Dispose();
        _latest = null;
        await _encoderTask.ConfigureAwait(false);
        await _audioTask.ConfigureAwait(false);
        if (_relayTask is not null) await _relayTask.ConfigureAwait(false);
        if (_app is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _app.StopAsync(timeout.Token).ConfigureAwait(false); }
            finally { await _app.DisposeAsync().ConfigureAwait(false); }
        }
        await _transportGate.WaitAsync().ConfigureAwait(false);
        try { if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false); }
        finally { _transportGate.Release(); }
    }

}
