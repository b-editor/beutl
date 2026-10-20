using System.Reactive.Subjects;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class EditViewModel : IContextCommandStateNotifier
{
    private readonly Subject<System.Reactive.Unit> _commandStateChangedSubject = new();
    private bool _commandStateNotifierDisposed;

    public IObservable<System.Reactive.Unit> CanExecuteChanged => _commandStateChangedSubject;

    // EditViewModel ctor で Player を初期化した直後に呼び、Player 系コマンドの ICommand.CanExecuteChanged を
    // 通知 Subject へ流す。これによりパレット表示中の再生状態遷移にも CanExecute が追従する。
    private void HookCommandStateNotifier()
    {
        foreach (System.Windows.Input.ICommand command in PlayerCommands())
        {
            command.CanExecuteChanged += OnPlayerCanExecuteChanged;
        }
    }

    private System.Windows.Input.ICommand[] PlayerCommands()
    {
        return [Player.PlayPause, Player.Next, Player.Previous, Player.Start, Player.End];
    }

    private void OnPlayerCanExecuteChanged(object? sender, EventArgs e)
    {
        if (!_commandStateNotifierDisposed)
        {
            _commandStateChangedSubject.OnNext(System.Reactive.Unit.Default);
        }
    }

    private void DisposeCommandStateNotifier()
    {
        _commandStateNotifierDisposed = true;
        foreach (System.Windows.Input.ICommand command in PlayerCommands())
        {
            command.CanExecuteChanged -= OnPlayerCanExecuteChanged;
        }

        _commandStateChangedSubject.Dispose();
    }

    // The frame the playhead is on, never before zero, and the name a new marker gets there.
    private (int Rate, TimeSpan Time, string DefaultName) GetMarkerPlacement()
    {
        int rate = Player.GetFrameRate();
        TimeSpan time = _editorClock.CurrentTime.Value.RoundToRate(rate);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return (rate, time, $"Marker {Scene.Markers.Count + 1}");
    }

    internal void ToggleMarkerAtCurrentTime()
    {
        (int rate, TimeSpan time, string defaultName) = GetMarkerPlacement();

        SceneMarker? existing = Scene.Markers
            .FirstOrDefault(m => m.Time.RoundToRate(rate) == time);
        if (existing != null)
        {
            Scene.Markers.Remove(existing);
            _logger.LogInformation("Removed marker at {Time}.", time);
        }
        else
        {
            Scene.Markers.Add(new SceneMarker(time, defaultName));
            _logger.LogInformation("Added marker at {Time}.", time);
        }

        HistoryManager.Commit();
    }

    internal void SeekToAdjacentMarker(bool forward)
    {
        TimeSpan current = _editorClock.CurrentTime.Value;
        TimeSpan? target = null;
        foreach (TimeSpan marker in Scene.Markers.Select(m => m.Time).Union([TimeSpan.Zero, Scene.Start, Scene.Duration + Scene.Start]))
        {
            if (forward)
            {
                if (marker > current
                    && (target == null || marker < target.Value))
                {
                    target = marker;
                }
            }
            else
            {
                if (marker < current
                    && (target == null || marker > target.Value))
                {
                    target = marker;
                }
            }
        }

        if (target != null)
        {
            SeekAndScroll(target.Value);
        }
    }

    internal void SeekToAdjacentKeyFrame(bool forward)
    {
        TimeSpan current = _editorClock.CurrentTime.Value;

        CoreObject? sel = _editorSelection.SelectedObject.Value;
        Element? scope = sel as Element
            ?? (sel as IHierarchical)?.FindHierarchicalParent<Element>();
        if (sel != null && scope == null)
        {
            _logger.LogDebug(
                "Key frame seek: selection ({Type}) could not be resolved to an Element; falling back to scene-wide search.",
                sel.GetType().Name);
        }
        IReadOnlyList<Element> roots = scope != null
            ? new[] { scope }
            : Scene.Children.ToArray();

        TimeSpan? target = KeyFrameNavigationHelper.FindAdjacentKeyFrame(roots, current, forward);

        if (target == null)
        {
            NotificationService.ShowInformation(
                forward ? Strings.NextKeyFrame : Strings.PreviousKeyFrame,
                Strings.NoKeyFrameToSeek);
            return;
        }

        SeekAndScroll(target.Value);
    }

    internal void SeekAndScroll(TimeSpan time)
    {
        _editorClock.Seek(time);

        if (FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            int currentZIndex = timeline.ToLayerNumber(timeline.Options.Value.Offset.Y);
            timeline.ScrollTo.Execute((new TimeRange(time, TimeSpan.FromTicks(1)), currentZIndex));
        }
    }
}
