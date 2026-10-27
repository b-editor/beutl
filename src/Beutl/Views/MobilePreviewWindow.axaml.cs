using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.PreviewStreaming;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using QRCoder;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace Beutl.Views;

public partial class MobilePreviewWindow : Window
{
    private readonly PlayerViewModel _player;
    private readonly CompositeDisposable _lifetime = [];
    private CompositeDisposable _streamSubscriptions = [];
    private IDisposable? _cleanPreview;
    private MobilePreviewServer? _server;
    private Bitmap? _qr;
    private CancellationTokenSource? _startupCancellation;
    private Task _startup = Task.CompletedTask;
    private Task? _shutdown;
    private bool _closed;

    public MobilePreviewWindow(PlayerViewModel player)
    {
        _player = player;
        InitializeComponent();
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .ThenByDescending(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => new NetworkAddress(n.Name, a.Address))).ToArray();
        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedIndex = addresses.Length > 0 ? 0 : -1;
        ToggleButton.IsEnabled = true;
        AddressBox.IsEnabled = false;
        StatusText.Text = Strings.MobilePreviewReady;
    }

    internal MobilePreviewServer? Server => _server;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var service = _player.EditViewModel.EditorService;
        var tab = service.SelectedTabItem.Value;
        if (tab?.Context.Value != _player.EditViewModel) { Close(); return; }
        _lifetime.Add(service.SelectedTabItem.Where(current => current != tab).Subscribe(_ => Close()));
        _lifetime.Add(tab.Context.Where(context => context != _player.EditViewModel).Subscribe(_ => Close()));
        _player.Disposing += OnPlayerDisposing;
    }

    private void OnPlayerDisposing(object? sender, EventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _lifetime.Dispose();
        _player.Disposing -= OnPlayerDisposing;
        _ = StopAfterCloseAsync();
        base.OnClosed(e);
    }

    private async Task StopAfterCloseAsync()
    {
        try { await StopSharingAsync(); }
        catch (Exception ex) { Log.CreateLogger<MobilePreviewWindow>().LogError(ex, "Could not stop mobile preview."); }
    }

    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        ToggleButton.IsEnabled = false;
        try
        {
            if (_server is not null) await StopSharingAsync();
            else if (InternetBox.IsChecked == true) await StartSharingAsync(null);
            else if (AddressBox.SelectedItem is NetworkAddress address) await StartSharingAsync(address.Address);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            StatusText.Text = Strings.MobilePreviewFailed;
            Log.CreateLogger<MobilePreviewWindow>().LogError(ex, "Could not start mobile preview.");
        }
        finally { ToggleButton.IsEnabled = !_closed && (InternetBox.IsChecked == true || AddressBox.SelectedItem is not null); }
    }

    internal Task StartSharingAsync(IPAddress? address)
    {
        if (_closed || _server is not null || !_startup.IsCompleted) return _startup;
        _shutdown = null;
        return _startup = StartCoreAsync(address);
    }

    private async Task StartCoreAsync(IPAddress? address)
    {
        using var cancellation = new CancellationTokenSource();
        _startupCancellation = cancellation;
        InternetBox.IsChecked = address is null;
        AddressBox.IsEnabled = false;
        InternetBox.IsEnabled = false;
        try
        {
            MobilePreviewServer server;
            if (address is null)
            {
                if (Owner?.DataContext is not MainViewModel main)
                    throw new InvalidOperationException("The account service is unavailable.");
                BeutlApiApplication api = main._beutlClients;
                if (api.AuthenticatedUser.Value is null) await api.SignInAsync(cancellation.Token);
                var session = await api.CreatePreviewSessionAsync(cancellation.Token);
                server = await MobilePreviewServer.StartRemoteAsync(session, cancellation.Token);
            }
            else server = await MobilePreviewServer.StartAsync(address, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested)
            {
                await server.DisposeAsync();
                return;
            }
            _server = server;
            server.ClientsChanged += OnClientsChanged;
            server.RelayDisconnected += OnRelayDisconnected;
            _streamSubscriptions = [];
            ConnectPlaybackControls(server);
            _streamSubscriptions.Add(_player.CleanPreviewImage.Subscribe(_ => PublishCurrent()));
            _streamSubscriptions.Add(((IPreviewPlayer)_player).AudioFramePushed.Subscribe(audio =>
            {
                if (_player.IsPlaying.Value && audio.PlaybackId == _player.PreviewPlaybackId)
                    server.PublishAudio(audio);
            }));
            _streamSubscriptions.Add(_player.ToneMappingMode.Subscribe(_ => PublishCurrent()));
            _streamSubscriptions.Add(_player.ToneMappingExposure.Subscribe(_ => PublishCurrent()));
            _streamSubscriptions.Add(_player.IsPlaying.Subscribe(playing =>
            {
                if (!playing) { server.PublishStopped(); PublishCurrent(); }
                PublishPlayback(server);
            }));
            using var data = QRCodeGenerator.GenerateQrCode(server.Endpoint.AbsoluteUri, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);
            using var png = new MemoryStream(qr.GetGraphic(6));
            _qr = new Bitmap(png);
            QrImage.Source = _qr;
            AddressText.Text = server.Endpoint.AbsoluteUri;
            UpdateCode();
            UpdateClients();
            SharingPanel.IsVisible = true;
            ToggleButton.Content = Strings.Stop;
            StatusText.Text = Strings.MobilePreviewRunning;
        }
        catch
        {
            await StopServerAsync();
            throw;
        }
        finally
        {
            _startupCancellation = null;
            AddressBox.IsEnabled = _server is null && InternetBox.IsChecked != true;
            InternetBox.IsEnabled = _server is null;
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
        catch (Exception ex) { Log.CreateLogger<MobilePreviewWindow>().LogError(ex, "Mobile preview startup failed."); }
        await StopServerAsync();
    }

    private async Task StopServerAsync()
    {
        var server = _server;
        _server = null;
        if (server is not null)
        {
            server.ClientsChanged -= OnClientsChanged;
            server.RelayDisconnected -= OnRelayDisconnected;
            server.ControlRequested = null;
        }
        _streamSubscriptions.Dispose();
        _cleanPreview?.Dispose();
        _cleanPreview = null;
        QrImage.Source = null;
        _qr?.Dispose();
        _qr = null;
        SharingPanel.IsVisible = false;
        ClientList.Children.Clear();
        ToggleButton.Content = Strings.Start;
        StatusText.Text = Strings.MobilePreviewReady;
        try { if (server is not null) await server.DisposeAsync(); }
        finally { AddressBox.IsEnabled = InternetBox.IsChecked != true; InternetBox.IsEnabled = true; }
    }

    private void OnClientsChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed && ReferenceEquals(sender, _server)) UpdateClients(); });

    private void UpdateClients()
    {
        if (_server is not { } server) return;
        var clients = server.GetClients();
        if (clients.Length == 0)
        {
            _cleanPreview?.Dispose();
            _cleanPreview = null;
        }
        else
        {
            _cleanPreview ??= _player.AcquireCleanPreview();
            PublishCurrent();
        }
        ClientsText.Text = string.Format(Strings.MobilePreviewClients, clients.Length);
        ClientList.Children.Clear();
        foreach (var client in clients)
        {
            var row = new DockPanel { LastChildFill = true };
            var disconnect = new Button { Content = Strings.Disconnect, HorizontalAlignment = HorizontalAlignment.Right };
            disconnect.Click += (_, _) => server.Disconnect(client.Id);
            DockPanel.SetDock(disconnect, Avalonia.Controls.Dock.Right);
            row.Children.Add(disconnect);
            row.Children.Add(new TextBlock { Text = client.Address, VerticalAlignment = VerticalAlignment.Center });
            ClientList.Children.Add(row);
        }
    }

    private void PublishCurrent()
    {
        var frame = _player.GetCleanPreviewFrame();
        using (frame.Image)
            _server?.Publish(frame.Image, _player.ToneMappingMode.Value, _player.ToneMappingExposure.Value,
                _player.IsPlaying.Value, frame.Time.ToSeconds(), frame.PlaybackId);
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (AddressBox is null || ToggleButton is null) return;
        AddressBox.IsEnabled = InternetBox.IsChecked != true;
        ToggleButton.IsEnabled = InternetBox.IsChecked == true || AddressBox.SelectedItem is not null;
    }

    private void OnRelayDisconnected(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closed || !ReferenceEquals(sender, _server)) return;
        await StopSharingAsync();
        StatusText.Text = Strings.MobilePreviewRelayDisconnected;
    });

    private void UpdateCode()
    {
        if (_server is not { } server) return;
        CodeText.Text = server.PairingCode;
        ExpiryText.Text = string.Format(Strings.MobilePreviewExpires, server.CodeExpires.LocalDateTime.ToString("t"));
    }

    private void OnRenewCodeClick(object? sender, RoutedEventArgs e) { _server?.RenewPairingCode(); UpdateCode(); }
    private void OnDisconnectAllClick(object? sender, RoutedEventArgs e) { _server?.DisconnectAll(); UpdateCode(); }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && _server is { } server)
            await clipboard.SetTextAsync(server.Endpoint.AbsoluteUri);
    }

    private sealed record NetworkAddress(string Name, IPAddress Address)
    {
        public override string ToString() => $"{Name} — {Address}";
    }
}
