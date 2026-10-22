using System.Reactive.Subjects;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    public Subject<Unit> BeginEditTimecodeRequested { get; } = new();

    public void RequestEditTimecode()
    {
        BeginEditTimecodeRequested.OnNext(Unit.Default);
    }

    public bool TryParseTimecode(string input, out MediaTime target, out GotoTimecodeError error)
    {
        target = MediaTime.Zero;
        if (Scene == null)
        {
            // The view should not raise the event without a scene; surface the
            // state mismatch so it is visible in telemetry.
            _logger.LogWarning(
                "TryParseTimecode invoked with no scene loaded. ({SceneId}, Input={Input})",
                _editViewModel.SceneId, input);
            error = GotoTimecodeError.NoScene;
            return false;
        }

        Rational rate = GetFrameRate();
        if (!GotoTimecodeParser.TryParse(input, rate, _editorClock.CurrentTime.Value, Scene.Markers, out MediaTime parsed, out error))
        {
            _logger.LogDebug(
                "Goto-timecode parse failed. ({SceneId}, Input={Input}, Error={Error})",
                _editViewModel.SceneId, input, error);
            return false;
        }

        // Snap here so a RoundToFrame overflow is reported now, not as a silent no-op seek after the editor closes.
        try
        {
            target = parsed.RoundToFrame(rate, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException ex)
        {
            _logger.LogWarning(ex,
                "RoundToFrame overflowed for goto-timecode target. ({SceneId}, Parsed={Parsed}, Rate={Rate})",
                _editViewModel.SceneId, parsed, rate);
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        MediaTime endTime = GetFirstAndLastFrames().Last;
        if (target < Scene.Start || target > endTime)
        {
            _logger.LogDebug(
                "Goto-timecode target out of scene range. ({SceneId}, Target={Target}, Range=[{Start}, {End}])",
                _editViewModel.SceneId, target, Scene.Start, endTime);
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        return true;
    }

    public static string GetTimecodeErrorMessage(GotoTimecodeError error) => error switch
    {
        GotoTimecodeError.InvalidFormat => Strings.GotoTimecode_InvalidFormat,
        GotoTimecodeError.MarkerNotFound => Strings.GotoTimecode_MarkerNotFound,
        GotoTimecodeError.NoScene => Strings.GotoTimecode_NoScene,
        GotoTimecodeError.OutOfRange => Strings.GotoTimecode_OutOfRange,
        _ => Strings.GotoTimecode_InvalidFormat,
    };

    public void ApplyTimecodeSeek(MediaTime target)
    {
        // target is already frame-snapped by TryParseTimecode.
        // 再生ヘッドがビューポート外へ飛ぶ場合に追従する。
        _editViewModel.SeekAndScroll(target);
    }
}
