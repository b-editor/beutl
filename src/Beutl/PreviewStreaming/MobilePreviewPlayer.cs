using Avalonia.Threading;
using Beutl.Editor.Services;
using Beutl.ViewModels;

namespace Beutl.PreviewStreaming;

// One sharing session's subscriptions, clean-frame lease and playback commands.
// Created and disposed on the UI thread; the tool context owns the server's lifetime.
internal sealed class MobilePreviewPlayer : IDisposable
{
    private readonly PlayerViewModel _player;
    private readonly MobilePreviewServer _server;
    private readonly CompositeDisposable _subscriptions = [];
    private IDisposable? _cleanPreview;
    private bool _disposed;

    internal MobilePreviewPlayer(PlayerViewModel player, MobilePreviewServer server)
    {
        _player = player;
        _server = server;
        try
        {
            server.ControlRequested = ExecuteControlAsync;
            server.ClientsChanged += OnClientsChanged;
            _subscriptions.Add(player.CleanPreviewImage.Subscribe(_ => PublishFrame()));
            _subscriptions.Add(((IPreviewPlayer)player).AudioFramePushed.Subscribe(audio =>
            {
                if (player.IsPlaying.Value && audio.PlaybackId == player.PreviewPlaybackId)
                    server.PublishAudio(audio);
            }));
            _subscriptions.Add(player.ToneMappingMode.Subscribe(_ => PublishFrame()));
            _subscriptions.Add(player.ToneMappingExposure.Subscribe(_ => PublishFrame()));
            _subscriptions.Add(player.IsPlaying.Subscribe(playing =>
            {
                if (!playing) { server.PublishStopped(); PublishFrame(); }
                PublishPlayback();
            }));
            _subscriptions.Add(player.CurrentFrame.Sample(TimeSpan.FromMilliseconds(100)).Subscribe(_ => PublishPlayback()));
            _subscriptions.Add(player.Duration.Subscribe(_ => PublishPlayback()));
            _subscriptions.Add(player.EditViewModel.IsEnabled.Subscribe(_ => PublishPlayback()));
            UpdateClients();
        }
        catch { Dispose(); throw; }
    }

    private void OnClientsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateClients);

    private void UpdateClients()
    {
        if (_disposed) return;
        if (_server.GetClients().Length == 0)
        {
            _cleanPreview?.Dispose();
            _cleanPreview = null;
        }
        else
        {
            _cleanPreview ??= _player.AcquireCleanPreview();
            PublishFrame();
        }
    }

    private void PublishFrame()
    {
        if (_disposed) return;
        var frame = _player.GetCleanPreviewFrame();
        using (frame.Image)
            _server.Publish(frame.Image, _player.ToneMappingMode.Value, _player.ToneMappingExposure.Value,
                _player.IsPlaying.Value, frame.Time.ToSeconds(), frame.PlaybackId);
    }

    private (double Minimum, double Maximum) GetSeekRange()
    {
        double minimum = Math.Min(0, Math.Min(_player.CurrentFrame.Value.ToSeconds(), _player.EditViewModel.Scene.Start.Value.ToSeconds()));
        return (minimum, Math.Max(minimum, _player.Duration.Value.ToSeconds()));
    }

    private Task<bool> ExecuteControlAsync(string id, PreviewControl command, CancellationToken ct)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool Available() => !_disposed && !ct.IsCancellationRequested
                && _server.IsClientConnected(id) && _player.EditViewModel.IsEnabled.Value;
            if (!Available()) return false;
            if (command.Action == "play") _player.Play();
            else
            {
                // Drain playback before seeking; its ticker would otherwise overwrite the new position.
                await _player.Pause();
                if (!Available()) return false;
                switch (command.Action)
                {
                    case "seek":
                        var (minimum, maximum) = GetSeekRange();
                        double position = Math.Clamp(command.Position, minimum, maximum);
                        _player.ApplyTimecodeSeek(MediaTime.FromSeconds(position).RoundToFrame(_player.GetFrameRate(), MidpointRounding.AwayFromZero));
                        break;
                    case "previous": _player.Previous.Execute(); break;
                    case "next": _player.Next.Execute(); break;
                }
            }
            PublishPlayback();
            return true;
        });

    private void PublishPlayback()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(PublishPlayback); return; }
        if (_disposed) return;
        var (minimum, maximum) = GetSeekRange();
        _server.PublishPlayback(_player.IsPlaying.Value, _player.CurrentFrame.Value.ToSeconds(), minimum,
            maximum, _player.GetFrameRate().ToDouble(), _player.EditViewModel.IsEnabled.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _server.ControlRequested = null;
        _server.ClientsChanged -= OnClientsChanged;
        _subscriptions.Dispose();
        _cleanPreview?.Dispose();
        _cleanPreview = null;
    }
}
