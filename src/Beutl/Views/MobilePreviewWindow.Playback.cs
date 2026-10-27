using Avalonia.Threading;
using Beutl.PreviewStreaming;

namespace Beutl.Views;

public partial class MobilePreviewWindow
{
    private void ConnectPlaybackControls(MobilePreviewServer server)
    {
        server.ControlRequested = (id, command, ct) => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool Available() => !_closed && ReferenceEquals(_server, server) && !ct.IsCancellationRequested
                && server.IsClientConnected(id) && _player.EditViewModel.IsEnabled.Value;
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
                        double minimum = Math.Min(0, Math.Min(_player.EditViewModel.Scene.Start.Value.ToSeconds(), _player.CurrentFrame.Value.ToSeconds()));
                        double position = Math.Clamp(command.Position, minimum, Math.Max(minimum, _player.Duration.Value.ToSeconds()));
                        _player.ApplyTimecodeSeek(MediaTime.FromSeconds(position).RoundToFrame(_player.GetFrameRate(), MidpointRounding.AwayFromZero));
                        break;
                    case "previous": _player.Previous.Execute(); break;
                    case "next": _player.Next.Execute(); break;
                }
            }
            PublishPlayback(server);
            return true;
        });
        _streamSubscriptions.Add(_player.CurrentFrame.Sample(TimeSpan.FromMilliseconds(100)).Subscribe(_ => PublishPlayback(server)));
        _streamSubscriptions.Add(_player.Duration.Subscribe(_ => PublishPlayback(server)));
        _streamSubscriptions.Add(_player.EditViewModel.IsEnabled.Subscribe(_ => PublishPlayback(server)));
    }

    private void PublishPlayback(MobilePreviewServer server)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => PublishPlayback(server)); return; }
        if (_closed || !ReferenceEquals(_server, server)) return;
        double position = _player.CurrentFrame.Value.ToSeconds();
        double minimum = Math.Min(0, Math.Min(position, _player.EditViewModel.Scene.Start.Value.ToSeconds()));
        server.PublishPlayback(_player.IsPlaying.Value, position, minimum,
            Math.Max(minimum, _player.Duration.Value.ToSeconds()), _player.GetFrameRate().ToDouble(), _player.EditViewModel.IsEnabled.Value);
    }
}
