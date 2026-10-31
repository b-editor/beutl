using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Logging;
using Beutl.PreviewStreaming;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using QRCoder;
using Reactive.Bindings;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace Beutl.ViewModels;

public sealed class MobilePreviewTabViewModel : IToolContext
{
    private readonly PlayerViewModel _player;
    private readonly CompositeDisposable _lifetime = [];
    private readonly DispatcherTimer _expiryTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private MobilePreviewPlayer? _previewPlayer;
    private MobilePreviewServer? _server;
    private Bitmap? _qr;
    private CancellationTokenSource? _startupCancellation;
    private Task _startup = Task.CompletedTask;
    private Task? _shutdown;
    private bool _closed;

    public MobilePreviewTabViewModel(EditViewModel editor)
    {
        _player = editor.Player;
        _expiryTimer.Tick += (_, _) => UpdateExpiry();
        IsActive = editor.EditorService.IsSelectedEditor(editor).ToReadOnlyReactivePropertySlim().DisposeWith(_lifetime);
        CanToggle = IsActive.CombineLatest(IsBusy, (active, busy) => active && !busy)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_lifetime);
        IsActive.Where(active => !active).Subscribe(active => { _ = StopSafelyAsync(); }).DisposeWith(_lifetime);
        _player.Disposing += OnPlayerDisposing;
    }

    public ToolTabExtension Extension => MobilePreviewTabExtension.Instance;
    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.MobilePreview);
    public IReadOnlyReactiveProperty<bool> IsActive { get; }
    public IReadOnlyReactiveProperty<bool> CanToggle { get; }
    public ReactivePropertySlim<bool> IsBusy { get; } = new();
    public ReactivePropertySlim<bool> IsSharing { get; } = new();
    public ReactivePropertySlim<bool> HasConnected { get; } = new();
    public ReactivePropertySlim<bool> ShowDetails { get; } = new();
    public ReactivePropertySlim<string> Status { get; } = new("");
    public ReactivePropertySlim<string> Address { get; } = new("");
    public ReactivePropertySlim<string> Code { get; } = new("");
    public ReactivePropertySlim<string> Expiry { get; } = new("");
    public ReactivePropertySlim<string> ClientsText { get; } = new("");
    public ReactivePropertySlim<Bitmap?> QrImage { get; } = new();
    public CoreList<MobilePreviewDevice> Clients { get; } = [];

    internal MobilePreviewServer? Server => _server;

    private void OnPlayerDisposing(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Dispose();
        _player.Disposing -= OnPlayerDisposing;
        _ = StopSafelyAsync();
    }

    public object? GetService(Type serviceType) => _player.EditViewModel.GetService(serviceType);
    public void ReadFromJson(JsonObject json) { }
    public void WriteToJson(JsonObject json) { }

    private async Task StopSafelyAsync()
    {
        try { await StopSharingAsync(); }
        catch (Exception ex) { Log.CreateLogger<MobilePreviewTabViewModel>().LogError(ex, "Could not stop mobile preview."); }
    }

    public async Task ToggleSharingAsync()
    {
        if (_closed || !CanToggle.Value) return;
        IsBusy.Value = true;
        try
        {
            if (_server is not null) await StopSharingAsync();
            else await StartSharingAsync();
        }
        catch (OperationCanceledException) when (_closed || !IsActive.Value) { }
        catch (Exception ex)
        {
            Status.Value = Strings.MobilePreviewFailed;
            Log.CreateLogger<MobilePreviewTabViewModel>().LogError(ex, "Could not start mobile preview.");
        }
        finally { IsBusy.Value = false; }
    }

    internal Task StartSharingAsync(PreviewSessionResponse? session = null)
    {
        if (_closed || !IsActive.Value || _server is not null || !_startup.IsCompleted) return _startup;
        _shutdown = null;
        return _startup = StartCoreAsync(session);
    }

    private async Task StartCoreAsync(PreviewSessionResponse? session)
    {
        using var cancellation = new CancellationTokenSource();
        _startupCancellation = cancellation;
        try
        {
            if (session is null)
            {
                var api = GetService(typeof(BeutlApiApplication)) as BeutlApiApplication
                    ?? throw new InvalidOperationException("The account service is unavailable.");
                if (api.AuthenticatedUser.Value is null) await api.SignInAsync(cancellation.Token);
                session = await api.CreatePreviewSessionAsync(cancellation.Token);
            }
            var server = await MobilePreviewServer.StartAsync(session, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested)
            {
                await server.DisposeAsync();
                return;
            }
            _server = server;
            server.ClientsChanged += OnClientsChanged;
            server.RelayDisconnected += OnRelayDisconnected;
            _previewPlayer = new MobilePreviewPlayer(_player, server);
            using var data = QRCodeGenerator.GenerateQrCode(server.Endpoint.AbsoluteUri, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);
            using var png = new MemoryStream(qr.GetGraphic(6));
            _qr = new Bitmap(png);
            QrImage.Value = _qr;
            Address.Value = server.Endpoint.AbsoluteUri;
            IsSharing.Value = true;
            UpdateCode();
            _expiryTimer.Start();
            UpdateClients();
            Status.Value = "";
        }
        catch
        {
            await StopServerAsync();
            throw;
        }
        finally
        {
            _startupCancellation = null;
        }
    }

    internal Task StopSharingAsync()
    {
        _startupCancellation?.Cancel();
        return _shutdown ??= StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        try { await _startup; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.CreateLogger<MobilePreviewTabViewModel>().LogError(ex, "Mobile preview startup failed."); }
        await StopServerAsync();
    }

    private async Task StopServerAsync()
    {
        _expiryTimer.Stop();
        var server = _server;
        _server = null;
        if (server is not null)
        {
            server.ClientsChanged -= OnClientsChanged;
            server.RelayDisconnected -= OnRelayDisconnected;
        }
        _previewPlayer?.Dispose();
        _previewPlayer = null;
        QrImage.Value = null;
        _qr?.Dispose();
        _qr = null;
        IsSharing.Value = false;
        HasConnected.Value = false;
        ShowDetails.Value = false;
        Clients.Clear();
        Status.Value = "";
        if (server is not null) await server.DisposeAsync();
    }

    private void OnClientsChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed && ReferenceEquals(sender, _server)) UpdateClients(); });

    private void UpdateClients()
    {
        if (_server is not { } server) return;
        var clients = server.GetClients();
        ClientsText.Value = string.Format(Strings.MobilePreviewClients, clients.Length);
        Clients.Clear();
        foreach (var client in server.GetDevices())
            Clients.Add(new(client.Id, client.Address, client.Connected));
        // Keep the details page after a disconnect, and leave explicitly reopened connection
        // information visible while the user pairs another device.
        if (clients.Length > 0 && !HasConnected.Value)
        {
            HasConnected.Value = true;
            ShowDetails.Value = true;
        }
    }

    private void OnRelayDisconnected(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closed || !ReferenceEquals(sender, _server)) return;
        await StopSafelyAsync();
        Status.Value = Strings.MobilePreviewRelayDisconnected;
    });

    private void UpdateCode()
    {
        if (_server is not { } server) return;
        Code.Value = server.PairingCode;
        UpdateExpiry();
    }

    private void UpdateExpiry()
    {
        if (_server is { } server)
            Expiry.Value = FormatExpiry(server.CodeExpires, DateTimeOffset.UtcNow);
    }

    internal static string FormatExpiry(DateTimeOffset expires, DateTimeOffset now)
    {
        if (expires <= now) return Strings.MobilePreviewCodeExpired;
        long seconds = (long)Math.Ceiling((expires - now).TotalSeconds);
        return string.Format(Strings.MobilePreviewExpires, FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00}"));
    }

    public void RenewCode() { _server?.RenewPairingCode(); UpdateCode(); }
    public void DisconnectAll() { _server?.DisconnectAll(); UpdateCode(); }
    public void Disconnect(string id) => _server?.Disconnect(id);
    public void ShowConnectionInfo() => ShowDetails.Value = false;
    public void ReturnToDetails() => ShowDetails.Value = HasConnected.Value;
}

public sealed record MobilePreviewDevice(string Id, string Name, bool Connected)
{
    public string Status => Connected ? Strings.MobilePreviewConnected : MissingMediaStrings.Offline;
}
