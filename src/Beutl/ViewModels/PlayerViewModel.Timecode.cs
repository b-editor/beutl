using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D.Gizmo;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Silk.NET.OpenAL;
using SkiaSharp;
using Vortice.Multimedia;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    public Subject<Unit> BeginEditTimecodeRequested { get; } = new();

    public void RequestEditTimecode()
    {
        BeginEditTimecodeRequested.OnNext(Unit.Default);
    }

    public bool TryParseTimecode(string input, out TimeSpan target, out GotoTimecodeError error)
    {
        target = TimeSpan.Zero;
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

        int rate = GetFrameRate();
        if (!GotoTimecodeParser.TryParse(input, rate, _editorClock.CurrentTime.Value, Scene.Markers, out TimeSpan parsed, out error))
        {
            _logger.LogDebug(
                "Goto-timecode parse failed. ({SceneId}, Input={Input}, Error={Error})",
                _editViewModel.SceneId, input, error);
            return false;
        }

        // Frame-snap up front so ApplyTimecodeSeek cannot fail later — without
        // this, the parser-accepted value could be returned to the view, the
        // editor would close on Accept(), and the actual seek would silently
        // no-op when RoundToRate overflowed.
        try
        {
            target = parsed.RoundToRate(rate);
        }
        catch (OverflowException ex)
        {
            _logger.LogWarning(ex,
                "RoundToRate overflowed for goto-timecode target. ({SceneId}, Parsed={Parsed}, Rate={Rate})",
                _editViewModel.SceneId, parsed, rate);
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        TimeSpan endTime = Scene.Start + Scene.Duration - TimeSpan.FromSeconds(1d / rate);
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

    public void ApplyTimecodeSeek(TimeSpan target)
    {
        // target is already frame-snapped by TryParseTimecode.
        // 再生ヘッドがビューポート外へ飛ぶ場合に追従する。
        _editViewModel.SeekAndScroll(target);
    }
}
