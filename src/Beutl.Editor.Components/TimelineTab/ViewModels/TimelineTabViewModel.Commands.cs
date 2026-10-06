using System.Collections.Specialized;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
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
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = ClickedFrame + TimeSpan.FromSeconds(1d / rate);
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetEnd(Scene, time);
    }

    private void OnSetStartTimeToCurrentTime()
    {
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetStart(Scene, CurrentTime.Value);
    }

    private void OnSetEndTimeToCurrentTime()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = CurrentTime.Value + TimeSpan.FromSeconds(1d / rate);
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetEnd(Scene, time);
    }

    public bool CanExecute(ContextCommandExecution execution)
    {
        return execution.CommandName switch
        {
            "Copy" or "Cut" or "Delete" or "Exclude" or "Duplicate" => SelectedElements.Count > 0,
            "NudgeLeftFrame" or "NudgeRightFrame"
                or "NudgeLeftLarge" or "NudgeRightLarge"
                or "NudgeLeftSecond" or "NudgeRightSecond" => SelectedElements.Count > 0,
            "CloseGap" => SelectedElements.Count > 0,
            "ToggleGroup" => SelectedElements.FirstOrDefault() is { } first
                && (first.CanUngroupSelectedElements() || first.CanGroupSelectedElements()),
            "ExitRazorMode" => IsRazorMode.Value,
            "ExitSlipMode" => IsSlipMode.Value,
            "ExitRollMode" => IsRollMode.Value,
            "ExitSlideMode" => IsSlideMode.Value,
            "Paste" or "SetStartTime" or "SetEndTime"
                or "ToggleRazorMode" or "ToggleRippleMode" or "ToggleSlipMode"
                or "ToggleRollMode" or "ToggleSlideMode"
                or "CloseAllGaps" or "GoToNextGap" or "GoToPreviousGap" => true,
            // Rename / Split など Execute で対応 case が無いコマンドは false を返し、
            // パレットやショートカット経路で誤って enabled として扱われないようにする。
            _ => false,
        };
    }

    public Task ExecuteAsync(ContextCommandExecution execution)
    {
        _logger.LogDebug("Executing context command {CommandName}.", execution.CommandName);

        // Nudge 連打を 1 Undo にまとめる debounce 中に他コマンドが Commit すると、
        // 双方が同じ transaction に積まれて 1 Undo で一緒に取り消されてしまう。
        // Nudge 以外のコマンドを実行する直前に Nudge 分を確定させて分離する。
        if (!execution.CommandName.StartsWith("Nudge", StringComparison.Ordinal))
        {
            FlushPendingNudgeCommit();
        }

        Task operation = Task.CompletedTask;
        switch (execution.CommandName)
        {
            case "Paste":
                operation = Paste.ExecuteAsync();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                    _logger.LogDebug("Paste command executed and KeyEventArgs handled.");
                }

                break;
            case "Duplicate":
                Duplicate.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "Copy":
                if (SelectedElements.FirstOrDefault() is { } copyTarget)
                {
                    operation = copyTarget.Copy.ExecuteAsync();
                }

                break;
            case "Cut":
                if (SelectedElements.FirstOrDefault() is { } cutTarget)
                {
                    operation = cutTarget.Cut.ExecuteAsync();
                }
                break;
            case "Delete":
                SelectedElements.FirstOrDefault()?.Delete.Execute();
                break;
            case "Exclude":
                SelectedElements.FirstOrDefault()?.Exclude.Execute();
                break;
            case "SetStartTime" when !IsTextInputFocused(execution.KeyEventArgs):
                SetStartTimeToCurrentTime.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "SetEndTime" when !IsTextInputFocused(execution.KeyEventArgs):
                SetEndTimeToCurrentTime.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ToggleGroup":
                var first = SelectedElements.FirstOrDefault();
                if (first?.CanUngroupSelectedElements() == true)
                {
                    first.UngroupSelectedElements.Execute();
                }
                else if (first?.CanGroupSelectedElements() == true)
                {
                    first.GroupSelectedElements.Execute();
                }

                break;
            case "ToggleRazorMode" when !IsTextInputFocused(execution.KeyEventArgs):
                EnterRazorMode();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ToggleRippleMode" when !IsTextInputFocused(execution.KeyEventArgs):
                IsRippleEnabled.Value = !IsRippleEnabled.Value;
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ExitRazorMode" when !IsTextInputFocused(execution.KeyEventArgs):
                if (IsRazorMode.Value)
                {
                    IsRazorMode.Value = false;
                    if (execution.KeyEventArgs != null)
                    {
                        execution.KeyEventArgs.Handled = true;
                    }
                }

                break;
            case "ToggleSlipMode" when !IsTextInputFocused(execution.KeyEventArgs):
                EnterTrimMode(IsSlipMode);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ToggleRollMode" when !IsTextInputFocused(execution.KeyEventArgs):
                EnterTrimMode(IsRollMode);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ToggleSlideMode" when !IsTextInputFocused(execution.KeyEventArgs):
                EnterTrimMode(IsSlideMode);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ExitSlipMode" when !IsTextInputFocused(execution.KeyEventArgs):
                if (IsSlipMode.Value)
                {
                    IsSlipMode.Value = false;
                    if (execution.KeyEventArgs != null)
                    {
                        execution.KeyEventArgs.Handled = true;
                    }
                }

                break;
            case "ExitRollMode" when !IsTextInputFocused(execution.KeyEventArgs):
                if (IsRollMode.Value)
                {
                    IsRollMode.Value = false;
                    if (execution.KeyEventArgs != null)
                    {
                        execution.KeyEventArgs.Handled = true;
                    }
                }

                break;
            case "ExitSlideMode" when !IsTextInputFocused(execution.KeyEventArgs):
                if (IsSlideMode.Value)
                {
                    IsSlideMode.Value = false;
                    if (execution.KeyEventArgs != null)
                    {
                        execution.KeyEventArgs.Handled = true;
                    }
                }

                break;
            case "NudgeLeftFrame" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Frame);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightFrame" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Frame);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeLeftLarge" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Large);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightLarge" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Large);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeLeftSecond" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Second);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightSecond" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Second);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "CloseGap" when !IsTextInputFocused(execution.KeyEventArgs):
                CloseGap.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "CloseAllGaps" when !IsTextInputFocused(execution.KeyEventArgs):
                CloseAllGaps.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "GoToNextGap" when !IsTextInputFocused(execution.KeyEventArgs):
                GoToNextGap.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "GoToPreviousGap" when !IsTextInputFocused(execution.KeyEventArgs):
                GoToPreviousGap.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
        }

        return operation;
    }

    // Timeline shortcuts that use printable keys must not fire while a text input has focus.
    // Checking only TextBox misses embedded text boxes inside wrappers such as AutoCompleteBox,
    // NumericUpDown, and MaskedTextBox, so inspect the Source visual ancestor chain.
    private static bool IsTextInputFocused(KeyEventArgs? args)
    {
        return IsTextInputSource(args?.Source);
    }

    internal static bool IsTextInputSource(object? source)
    {
        if (source is not Visual visual) return false;
        return visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null;
    }

    private void EnterRazorMode()
    {
        bool next = !IsRazorMode.Value;
        IsRazorMode.Value = false;
        IsSlipMode.Value = false;
        IsRollMode.Value = false;
        IsSlideMode.Value = false;
        IsRazorMode.Value = next;
    }

    private void EnterTrimMode(ReactivePropertySlim<bool> mode)
    {
        bool next = !mode.Value;
        IsRazorMode.Value = false;
        IsSlipMode.Value = false;
        IsRollMode.Value = false;
        IsSlideMode.Value = false;
        mode.Value = next;
    }

    private enum NudgeUnit { Frame, Large, Second }

    private void NudgeSelectedElements(int direction, NudgeUnit unit)
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

        int rate = Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30;
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

    private void FlushPendingNudgeCommit()
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

    public void RazorSplitAt(TimeSpan time, bool acrossAllLayers)
    {
        IReadOnlyList<ElementViewModel> targets = acrossAllLayers
            ? Elements.Where(e => e.Model.Range.Contains(time)).ToArray()
            : Elements.Where(e => e.Model.Range.Contains(time) && e.Model.ZIndex == CalculateClickedLayer()).ToArray();

        if (targets.Count == 0)
        {
            return;
        }

        targets[0].SplitAt(targets, time);
    }
}
