using System.Reactive.Subjects;
using Avalonia.Controls;
using Beutl.Animation;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class EditViewModel : IContextCommandHandler, IContextCommandStateNotifier
{
    private readonly Subject<System.Reactive.Unit> _commandStateChangedSubject = new();
    private bool _commandStateNotifierDisposed;

    public IObservable<System.Reactive.Unit> CanExecuteChanged => _commandStateChangedSubject;

    // EditViewModel ctor で Player を初期化した直後に呼び、Player 系コマンドの ICommand.CanExecuteChanged を
    // 通知 Subject へ流す。これによりパレット表示中の再生状態遷移にも CanExecute が追従する。
    private void HookCommandStateNotifier()
    {
        ((System.Windows.Input.ICommand)Player.PlayPause).CanExecuteChanged += OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Next).CanExecuteChanged += OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Previous).CanExecuteChanged += OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Start).CanExecuteChanged += OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.End).CanExecuteChanged += OnPlayerCanExecuteChanged;
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
        ((System.Windows.Input.ICommand)Player.PlayPause).CanExecuteChanged -= OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Next).CanExecuteChanged -= OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Previous).CanExecuteChanged -= OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.Start).CanExecuteChanged -= OnPlayerCanExecuteChanged;
        ((System.Windows.Input.ICommand)Player.End).CanExecuteChanged -= OnPlayerCanExecuteChanged;
        _commandStateChangedSubject.Dispose();
    }

    // IContextCommandHandler を実装しない場合、以下のように ContextCommandAttribute を使用してコマンドを定義します。
    // [ContextCommand]
    // public void PlayPause(KeyEventArgs e)
    // {
    //     if (e.Source is TextBox)
    //         return;
    //     Player.PlayPause.Execute();
    //     e.Handled = true;
    // }
    //
    // [ContextCommand]
    // public void Next()
    // {
    //     Player.Next.Execute();
    // }
    //
    // [ContextCommand]
    // public void Previous()
    // {
    //     Player.Previous.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekStart()
    // {
    //     Player.Start.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekEnd()
    // {
    //     Player.End.Execute();
    // }

    public bool CanExecute(ContextCommandExecution execution)
    {
        return execution.CommandName switch
        {
            "PlayPause" => ((System.Windows.Input.ICommand)Player.PlayPause).CanExecute(null),
            "Next" => ((System.Windows.Input.ICommand)Player.Next).CanExecute(null),
            "Previous" => ((System.Windows.Input.ICommand)Player.Previous).CanExecute(null),
            "SeekStart" => ((System.Windows.Input.ICommand)Player.Start).CanExecute(null),
            "SeekEnd" => ((System.Windows.Input.ICommand)Player.End).CanExecute(null),
            _ => true,
        };
    }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        bool isFromTextBox = execution.KeyEventArgs?.Source is TextBox;
        switch (execution.CommandName)
        {
            case "PlayPause" when !isFromTextBox:
                Player.PlayPause.Execute();
                break;
            case "Next":
                Player.Next.Execute();
                break;
            case "Previous":
                Player.Previous.Execute();
                break;
            case "SeekStart":
                Player.Start.Execute();
                break;
            case "SeekEnd":
                Player.End.Execute();
                break;
            case "ShuttleForward" when !isFromTextBox:
                Player.ShuttleForward();
                break;
            case "ShuttleForwardFine" when !isFromTextBox:
                Player.ShuttleForward(fineGrain: true);
                break;
            case "ShuttleBackward" when !isFromTextBox:
                Player.ShuttleBackward();
                break;
            case "ShuttleBackwardFine" when !isFromTextBox:
                Player.ShuttleBackward(fineGrain: true);
                break;
            case "ShuttleStop" when !isFromTextBox:
                Player.ShuttleStop();
                break;
            case "ToggleLoop" when !isFromTextBox:
                Player.ToggleLoop();
                break;
            case "ToggleMarker" when !isFromTextBox:
                ToggleMarkerAtCurrentTime();
                break;
            case "NextMarker" when !isFromTextBox:
                SeekToAdjacentMarker(forward: true);
                break;
            case "PreviousMarker" when !isFromTextBox:
                SeekToAdjacentMarker(forward: false);
                break;
            case "GotoTimecode" when !isFromTextBox:
                Player.RequestEditTimecode();
                break;
            case "NextKeyFrame" when !isFromTextBox:
                SeekToAdjacentKeyFrame(forward: true);
                break;
            case "PreviousKeyFrame" when !isFromTextBox:
                SeekToAdjacentKeyFrame(forward: false);
                break;
            case "ToggleOnionSkin" when !isFromTextBox:
                Player.IsOnionSkinEnabled.Value = !Player.IsOnionSkinEnabled.Value;
                break;
            default:
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }

    private void ToggleMarkerAtCurrentTime()
    {
        int rate = Player.GetFrameRate();
        TimeSpan time = _editorClock.CurrentTime.Value.RoundToRate(rate);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        SceneMarker? existing = Scene.Markers
            .FirstOrDefault(m => m.Time.RoundToRate(rate) == time);
        if (existing != null)
        {
            Scene.Markers.Remove(existing);
            _logger.LogInformation("Removed marker at {Time}.", time);
        }
        else
        {
            Scene.Markers.Add(new SceneMarker(time, $"Marker {Scene.Markers.Count + 1}"));
            _logger.LogInformation("Added marker at {Time}.", time);
        }

        HistoryManager.Commit();
    }

    private void SeekToAdjacentMarker(bool forward)
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

    private void SeekToAdjacentKeyFrame(bool forward)
    {
        TimeSpan current = _editorClock.CurrentTime.Value;

        // 探索範囲のフォールバックチェーン: 選択中の Element → 直近の親 Element → Scene 全 Element (Scene のみ複数 root)
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

        TimeSpan? target = null;
        foreach (Element el in roots)
        {
            foreach ((KeyFrameAnimation anim, TimeSpan offset) in EnumerateKeyFrameAnimations(el))
            {
                foreach (IKeyFrame kf in anim.KeyFrames)
                {
                    TimeSpan time = kf.KeyTime + offset;
                    bool inDirection = forward ? time > current : time < current;
                    bool isCloser = target == null
                        || (forward ? time < target.Value : time > target.Value);
                    if (inDirection && isCloser)
                        target = time;
                }
            }
        }

        if (target == null)
        {
            NotificationService.ShowInformation(
                forward ? Strings.NextKeyFrame : Strings.PreviousKeyFrame,
                Strings.NoKeyFrameToSeek);
            return;
        }

        SeekAndScroll(target.Value);
    }

    private void SeekAndScroll(TimeSpan time)
    {
        _editorClock.CurrentTime.Value = time;

        if (FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            int currentZIndex = timeline.ToLayerNumber(timeline.Options.Value.Offset.Y);
            timeline.ScrollTo.Execute((new TimeRange(time, TimeSpan.FromTicks(1)), currentZIndex));
        }
    }

    // KeyFrameAnimation をタイムライン絶対時刻に正規化するための offset と一緒に列挙する。
    //   1. EngineObject.Properties.Animation (通常プロパティ): AnimatableProperty.SetOwnerObject
    //      で animation が hierarchical child として attach されるため、所有 EngineObject の
    //      TimeRange.Start を offset に使う (UseGlobalClock=false 時)。これは
    //      KeyFrameAnimation<T>.GetAnimatedValue の挙動と一致する。
    //   2. INodeMember.Property.Animation (ノードグラフ): NodePropertyAdapter.Animation は
    //      hierarchical child として attach されないため、animation 経由では owner を辿れない。
    //      代わりに NodeMember の親 GraphNode を辿り、GraphSnapshot.LoadAnimatedValues
    //      ("time - node.Start" でローカル時刻を解釈する) と同じ offset を使う。
    //      IEnginePropertyBackedInputPort はエンジン側プロパティが pass 1 で拾われるため、
    //      二重カウントしないよう除外する。
    private static IEnumerable<(KeyFrameAnimation Animation, TimeSpan Offset)>
        EnumerateKeyFrameAnimations(Element root)
    {
        var visited = new HashSet<KeyFrameAnimation>();

        var enginePass = new ObjectSearcher(root, v => v is EngineObject);
        foreach (EngineObject obj in enginePass.SearchAll().OfType<EngineObject>())
        {
            foreach (IProperty property in obj.Properties)
            {
                if (property.Animation is KeyFrameAnimation kfa && visited.Add(kfa))
                {
                    TimeSpan offset = kfa.UseGlobalClock
                        ? TimeSpan.Zero
                        : kfa.FindHierarchicalParent<EngineObject>()?.TimeRange.Start ?? TimeSpan.Zero;
                    yield return (kfa, offset);
                }
            }
        }

        var memberPass = new ObjectSearcher(root, v => v is INodeMember);
        foreach (INodeMember member in memberPass.SearchAll().OfType<INodeMember>())
        {
            if (member is IEnginePropertyBackedInputPort)
                continue;

            if (member.Property is IAnimatablePropertyAdapter { Animation: KeyFrameAnimation kfa }
                && visited.Add(kfa))
            {
                TimeSpan offset = kfa.UseGlobalClock
                    ? TimeSpan.Zero
                    : member.FindHierarchicalParent<GraphNode>()?.Start ?? TimeSpan.Zero;
                yield return (kfa, offset);
            }
        }
    }
}
