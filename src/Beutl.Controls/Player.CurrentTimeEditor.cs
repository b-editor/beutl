using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging;

namespace Beutl.Controls;

public partial class Player
{
    /// <summary>
    /// Enters timecode-editing mode for the current time display. No-op if the
    /// control template has not been applied yet.
    /// </summary>
    public void BeginEditCurrentTime()
    {
        if (_currentTimeTextBox == null) return;

        _currentTimeTextBox.Classes.Remove("invalid");
        ToolTip.SetTip(_currentTimeTextBox, null);
        _currentTimeTextBox.Text = _currentTime;
        // Snapshot the text the editor was opened with; CurrentTime is bound to
        // the live playhead and may advance under the user during playback, so
        // SubmitCurrentTimeEdit cannot compare against the latest _currentTime
        // to decide whether the user edited the value.
        _editStartText = _currentTime;
        _currentTimeTextBox.IsVisible = true;
        if (_currentTimeTextBlock != null)
        {
            _currentTimeTextBlock.IsVisible = false;
        }
        CloseMarkerPopup();
        _currentTimeTextBox.Focus();
        _currentTimeTextBox.SelectAll();
    }

    private void EndEditCurrentTime()
    {
        if (_currentTimeTextBox == null) return;
        _currentTimeTextBox.IsVisible = false;
        _currentTimeTextBox.Classes.Remove("invalid");
        ToolTip.SetTip(_currentTimeTextBox, null);
        if (_currentTimeTextBlock != null)
        {
            _currentTimeTextBlock.IsVisible = true;
        }
        CloseMarkerPopup();
    }

    private void CloseMarkerPopup()
    {
        if (_markerPopup != null)
        {
            _markerPopup.IsOpen = false;
        }
    }

    private bool IsMarkerPopupOpen => _markerPopup != null && _markerPopup.IsOpen;

    private void UpdateMarkerPopup(string? text)
    {
        if (_markerPopup == null || _markerListBox == null) return;
        if (string.IsNullOrEmpty(text) || text[0] != '@')
        {
            _markerPopup.IsOpen = false;
            return;
        }

        // Match the prefix-trimming rule used by GotoTimecodeParser.TryParseMarker
        // so the popup never lists an entry the parser would reject.
        string prefix = text.Substring(1).TrimStart();
        IReadOnlyList<PlayerMarkerEntry>? source = Markers;
        if (source == null || source.Count == 0)
        {
            _markerPopup.IsOpen = false;
            return;
        }

        var filtered = new List<PlayerMarkerEntry>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            PlayerMarkerEntry m = source[i];
            if (m == null) continue;
            string name = m.Name ?? string.Empty;
            if (prefix.Length == 0
                || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(m);
            }
        }

        if (filtered.Count == 0)
        {
            _markerPopup.IsOpen = false;
            return;
        }

        _markerListBox.ItemsSource = filtered;
        _markerListBox.SelectedIndex = 0;
        _markerPopup.IsOpen = true;
    }

    private void CommitMarkerSelection()
    {
        if (_markerListBox == null || _currentTimeTextBox == null || _markerPopup == null) return;
        if (_markerListBox.SelectedItem is not PlayerMarkerEntry selected) return;

        _currentTimeTextBox.Text = "@" + (selected.Name ?? string.Empty);
        _markerPopup.IsOpen = false;
        SubmitCurrentTimeEdit();
    }

    private void SubmitCurrentTimeEdit()
    {
        if (_currentTimeTextBox == null) return;
        string input = _currentTimeTextBox.Text ?? string.Empty;

        // No-op when the user pressed Enter without editing the displayed text.
        // The CurrentTime string format (hh\:mm\:ss\.ff) day-wraps for timelines
        // >= 24h, so re-parsing it would silently jump the playhead backwards
        // by one day. Compare against the snapshot taken at edit start, not the
        // live _currentTime, because the playhead may have advanced while the
        // editor was open during playback.
        if (string.Equals(input, _editStartText, StringComparison.Ordinal))
        {
            EndEditCurrentTime();
            return;
        }

        var handler = CurrentTimeSubmitted;

        if (handler == null)
        {
            // The editor stays open with the fallback tooltip below; surface the
            // misconfiguration so it is detectable in telemetry rather than
            // silently masquerading as a user input error.
            s_logger.LogWarning("CurrentTimeSubmitted has no subscribers; timecode '{Input}' cannot be processed.", input);
        }

        TimecodeSubmittedEventArgs args = new TimecodeSubmittedEventArgs(input);
        try
        {
            handler?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            // Subscribers must not let exceptions escape into the Avalonia
            // event loop (it would either crash the app or be swallowed by the
            // global unhandled-exception handler with no UI feedback). Log the
            // exception so the stack trace is recoverable, then reject the
            // submission and let the view show the fallback tooltip below.
            s_logger.LogError(ex, "A CurrentTimeSubmitted subscriber threw while processing '{Input}'.", input);
            args.Reject(string.Empty);
        }

        if (args.Handled)
        {
            EndEditCurrentTime();
            return;
        }

        _currentTimeTextBox.Classes.Add("invalid");
        ToolTip.SetTip(_currentTimeTextBox, !string.IsNullOrEmpty(args.Error) ? args.Error : Beutl.Language.MessageStrings.UnexpectedError);
        _currentTimeTextBox.SelectAll();
    }

    private void OnCurrentTimeTextBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentTimeTextBlock != null && e.GetCurrentPoint(_currentTimeTextBlock).Properties.IsLeftButtonPressed)
        {
            BeginEditCurrentTime();
            e.Handled = true;
        }
    }

    private void OnCurrentTimeTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // IME 変換中のキー入力は IME に委ね、ポップアップ操作・確定処理に横取りしない。
        if (e.Key == Key.ImeProcessed) return;

        if (IsMarkerPopupOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveMarkerSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveMarkerSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                    CommitMarkerSelection();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    // ポップアップだけ閉じ、編集モードは継続する。
                    CloseMarkerPopup();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Enter)
        {
            SubmitCurrentTimeEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEditCurrentTime();
            e.Handled = true;
        }
    }

    private void MoveMarkerSelection(int delta)
    {
        if (_markerListBox == null) return;
        int count = _markerListBox.ItemCount;
        if (count == 0) return;
        int current = _markerListBox.SelectedIndex;
        int next = Math.Clamp(current + delta, 0, count - 1);
        if (next != current)
        {
            _markerListBox.SelectedIndex = next;
        }
        if (_markerListBox.SelectedItem is { } item)
        {
            _markerListBox.ScrollIntoView(item);
        }
    }

    private void OnMarkerListBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is not Visual source) return;

        // 起点アイテム経由で取得することで、SelectedItem 反映前のクリックでも誤確定しない。
        ListBoxItem? item = FindListBoxItem(source);
        if (item == null || item.DataContext is not PlayerMarkerEntry entry) return;

        if (_markerListBox == null) return;
        _markerListBox.SelectedItem = entry;
        e.Handled = true;
        CommitMarkerSelection();
    }

    private static ListBoxItem? FindListBoxItem(Visual source)
    {
        Visual? current = source;
        while (current != null)
        {
            if (current is ListBoxItem item) return item;
            current = current.GetVisualParent();
        }
        return null;
    }

    private void OnCurrentTimeTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_currentTimeTextBox != null && _currentTimeTextBox.IsVisible)
        {
            EndEditCurrentTime();
        }
    }
}
