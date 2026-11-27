using System.Diagnostics;
using Avalonia.Threading;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private static readonly TimeSpan s_escapeWindow = TimeSpan.FromMilliseconds(1500);
    private Guid? _escapeStopTurn;
    private long _escapeArmedAt;
    private IDisposable? _escapeTimer;

    public bool ShowEscapeStopHint => _escapeStopTurn.HasValue;
    public string NewThreadTooltip => FormatPrimaryShortcut(AgentStrings.NewThread, "N");
    public string ThreadPickerTooltip => HasBackgroundThreadActivity
        ? $"{FormatPrimaryShortcut(ThreadTitle, "K")}\n{AgentStrings.BackgroundThreadActivity}"
        : FormatPrimaryShortcut(ThreadTitle, "K");
    public string ModelPickerTooltip => FormatPrimaryShortcut(ProviderTooltip, "M");
    public string PermissionModeTooltip => string.Format(AgentStrings.ShortcutTooltip, SelectedPermissionMode.Label, "Shift+Tab");
    public string StopTooltip => string.Format(AgentStrings.ShortcutTooltip, AgentStrings.Stop, AgentStrings.StopShortcutKeys);

    private static string FormatPrimaryShortcut(string label, string key)
        => string.Format(AgentStrings.ShortcutTooltip, label, $"{(OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl")}+{key}");

    internal Task CyclePermissionModeAsync()
    {
        for (int index = 0; index < PermissionModes.Count; index++)
        {
            if (PermissionModes[index] == SelectedPermissionMode)
                return SetPermissionModeAsync(PermissionModes[(index + 1) % PermissionModes.Count]);
        }
        return Task.CompletedTask;
    }

    internal Task ConfirmStopShortcutAsync()
    {
        if (!CanStop || _controller?.ActiveTurnId is not { } turnId)
            return Task.CompletedTask;
        if (_escapeStopTurn == turnId && Stopwatch.GetElapsedTime(_escapeArmedAt) < s_escapeWindow)
        {
            ClearEscapeStopHint();
            return StopAsync();
        }

        _escapeTimer?.Dispose();
        _escapeStopTurn = turnId;
        _escapeArmedAt = Stopwatch.GetTimestamp();
        _escapeTimer = DispatcherTimer.RunOnce(ClearEscapeStopHint, s_escapeWindow);
        Notify(nameof(ShowEscapeStopHint));
        return Task.CompletedTask;
    }

    internal void ClearEscapeStopHint()
    {
        _escapeTimer?.Dispose();
        _escapeTimer = null;
        if (_escapeStopTurn.HasValue)
        {
            _escapeStopTurn = null;
            Notify(nameof(ShowEscapeStopHint));
        }
    }
}
