using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    private void SubscribeToolMode(ReactivePropertySlim<bool> mode)
    {
        mode.Subscribe(isEnabled =>
            {
                if (isEnabled)
                {
                    EnforceSingleToolMode(mode);
                }

                RaiseCanExecuteChanged();
            })
            .AddTo(_disposables);
    }

    private void EnforceSingleToolMode(ReactivePropertySlim<bool> activeMode)
    {
        if (_updatingToolMode) return;

        _updatingToolMode = true;
        try
        {
            if (!ReferenceEquals(activeMode, IsRazorMode)) IsRazorMode.Value = false;
            if (!ReferenceEquals(activeMode, IsSlipMode)) IsSlipMode.Value = false;
            if (!ReferenceEquals(activeMode, IsRollMode)) IsRollMode.Value = false;
            if (!ReferenceEquals(activeMode, IsSlideMode)) IsSlideMode.Value = false;
        }
        finally
        {
            _updatingToolMode = false;
        }
    }

    private void OnSetStartTimeToPointerPosition()
    {
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetStart(Scene, ClickedFrame);
    }

    private void OnSetEndTimeToPointerPosition()
    {
        SetSceneEndAfterFrame(ClickedFrame);
    }

    private void OnSetStartTimeToCurrentTime()
    {
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetStart(Scene, CurrentTime.Value);
    }

    private void OnSetEndTimeToCurrentTime()
    {
        SetSceneEndAfterFrame(CurrentTime.Value);
    }

    // Ending the scene at a frame keeps that frame inside it, so the end lands one frame later.
    private void SetSceneEndAfterFrame(TimeSpan frame)
    {
        TimeSpan time = frame + SceneTimeRangeService.GetFrameDuration(Scene);
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetEnd(Scene, time);
    }

    // Timeline shortcuts that use printable keys must not fire while a text input has focus.
    // Checking only TextBox misses embedded text boxes inside wrappers such as AutoCompleteBox,
    // NumericUpDown, and MaskedTextBox, so inspect the Source visual ancestor chain.
    internal static bool IsTextInputSource(object? source)
    {
        if (source is not Visual visual) return false;
        return visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null;
    }

    internal enum NudgeUnit { Frame, Large, Second }

    internal void NudgeSelectedElements(int direction, NudgeUnit unit)
    {
        // Anchor on the leftmost selected element so the resulting delta lands
        // on the frame grid regardless of HashSet iteration order.
        ElementViewModel? first = SelectedElements
            .OrderBy(e => e.Model.Start)
            .ThenBy(e => e.Model.ZIndex)
            .FirstOrDefault();
        if (first is null) return;

        ElementViewModel[] targets = first.GetGroupOrSelectedElements()
            .Where(x => x.IsEditable.Value)
            .ToArray();
        if (targets.Length == 0) return;

        int rate = SceneTimeRangeService.GetFrameRate(Scene);
        int frames = unit switch
        {
            NudgeUnit.Frame => direction,
            NudgeUnit.Large => direction * 10,
            NudgeUnit.Second => direction * rate,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unhandled NudgeUnit."),
        };

        EditorContext.GetRequiredService<IElementNudgeService>()
            .Nudge(Scene, targets.Select(x => x.Model).ToArray(), frames);
    }

    internal void FlushPendingNudgeCommit()
    {
        if (_isDisposed) return;
        try
        {
            EditorContext.GetRequiredService<IElementNudgeService>().Flush();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Pending nudge flush dropped: editor context already disposed.");
        }
    }

    public void RazorSplitAt(TimeSpan time)
    {
        Element[] models = Elements
            .Where(e => e.Model.Range.Contains(time) && e.IsEditable.Value)
            .Select(e => e.Model)
            .ToArray();
        if (models.Length == 0) return;

        EditorContext.GetRequiredService<IElementStructureService>()
            .Split(Scene, models, time.RoundToRate(SceneTimeRangeService.GetFrameRate(Scene)));
    }
}
