using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Beutl.Api;
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
    private MobilePreviewPlayer? _previewPlayer;
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
        _ = StopSafelyAsync();
        base.OnClosed(e);
    }

    private async Task StopSafelyAsync()
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
            else await StartSharingAsync();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            StatusText.Text = Strings.MobilePreviewFailed;
            Log.CreateLogger<MobilePreviewWindow>().LogError(ex, "Could not start mobile preview.");
        }
        finally { ToggleButton.IsEnabled = !_closed; }
    }

    internal Task StartSharingAsync(PreviewSessionResponse? session = null)
    {
        if (_closed || _server is not null || !_startup.IsCompleted) return _startup;
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
                if (Owner?.DataContext is not MainViewModel main)
                    throw new InvalidOperationException("The account service is unavailable.");
                BeutlApiApplication api = main._beutlClients;
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
        }
        _previewPlayer?.Dispose();
        _previewPlayer = null;
        QrImage.Source = null;
        _qr?.Dispose();
        _qr = null;
        SharingPanel.IsVisible = false;
        ClientList.Children.Clear();
        ToggleButton.Content = Strings.Start;
        StatusText.Text = Strings.MobilePreviewReady;
        if (server is not null) await server.DisposeAsync();
    }

    private void OnClientsChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed && ReferenceEquals(sender, _server)) UpdateClients(); });

    private void UpdateClients()
    {
        if (_server is not { } server) return;
        var clients = server.GetClients();
        ClientsText.Text = string.Format(Strings.MobilePreviewClients, clients.Length);
        ClientList.Children.Clear();
        foreach (var client in server.GetDevices())
        {
            var row = new DockPanel { LastChildFill = true };
            var disconnect = new Button { Content = Strings.Disconnect, HorizontalAlignment = HorizontalAlignment.Right };
            disconnect.Click += (_, _) => server.Disconnect(client.Id);
            DockPanel.SetDock(disconnect, Avalonia.Controls.Dock.Right);
            row.Children.Add(disconnect);
            row.Children.Add(new TextBlock
            {
                Text = client.Connected ? client.Address : $"{client.Address} ({MissingMediaStrings.Offline})",
                VerticalAlignment = VerticalAlignment.Center
            });
            ClientList.Children.Add(row);
        }
    }

    private void OnRelayDisconnected(object? sender, EventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (_closed || !ReferenceEquals(sender, _server)) return;
        await StopSafelyAsync();
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

}
