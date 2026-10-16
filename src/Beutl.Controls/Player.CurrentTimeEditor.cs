using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Beutl.Controls;

internal partial class Player
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
        // Playback advances CurrentTime, so SubmitCurrentTimeEdit compares with the text at edit start.
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
            if (prefix.Length == 0
                || m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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

        _currentTimeTextBox.Text = "@" + selected.Name;
        _markerPopup.IsOpen = false;
        SubmitCurrentTimeEdit();
    }

    private void SubmitCurrentTimeEdit()
    {
        if (_currentTimeTextBox == null) return;
        string input = _currentTimeTextBox.Text ?? string.Empty;

        // Enter without an edit does nothing. The display format wraps at 24 hours, so re-parsing it
        // would move the playhead back a day.
        if (string.Equals(input, _editStartText, StringComparison.Ordinal))
        {
            EndEditCurrentTime();
            return;
        }

        TimecodeSubmittedEventArgs args = new TimecodeSubmittedEventArgs(input);
        CurrentTimeSubmitted?.Invoke(this, args);

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

    private void DetachCurrentTimeEditor()
    {
        // Detach handlers / dispose subscriptions from any previous template so
        // re-applying the template (e.g. when the control template is reassigned)
        // doesn't double-wire handlers or leak the observable subscription.
        if (_currentTimeTextBlock != null)
        {
            _currentTimeTextBlock.PointerPressed -= OnCurrentTimeTextBlockPointerPressed;
        }
        if (_currentTimeTextBox != null)
        {
            _currentTimeTextBox.KeyDown -= OnCurrentTimeTextBoxKeyDown;
            _currentTimeTextBox.LostFocus -= OnCurrentTimeTextBoxLostFocus;
        }
        if (_markerListBox != null)
        {
            _markerListBox.PointerReleased -= OnMarkerListBoxPointerReleased;
        }
        _currentTimeTextBoxTextSubscription?.Dispose();
        _currentTimeTextBoxTextSubscription = null;
        _markersChangeSubscription?.Dispose();
        _markersChangeSubscription = null;
    }

    private void AttachCurrentTimeEditor()
    {
        if (_currentTimeTextBlock != null)
        {
            _currentTimeTextBlock.PointerPressed += OnCurrentTimeTextBlockPointerPressed;
        }

        if (_currentTimeTextBox != null)
        {
            _currentTimeTextBox.IsVisible = false;
            _currentTimeTextBox.KeyDown += OnCurrentTimeTextBoxKeyDown;
            _currentTimeTextBox.LostFocus += OnCurrentTimeTextBoxLostFocus;
            _currentTimeTextBoxTextSubscription = _currentTimeTextBox
                .GetObservable(TextBox.TextProperty)
                .Subscribe(text =>
                {
                    _currentTimeTextBox.Classes.Remove("invalid");
                    ToolTip.SetTip(_currentTimeTextBox, null);
                    // TextBox が非表示の間（編集モード外）のスプリアス通知でポップアップを開かない。
                    if (_currentTimeTextBox.IsVisible)
                    {
                        UpdateMarkerPopup(text);
                    }
                });
        }

        if (_markerListBox != null)
        {
            _markerListBox.PointerReleased += OnMarkerListBoxPointerReleased;
        }

        // マーカーソースが差し替わったら現在の入力に基づいて再フィルタする。
        _markersChangeSubscription = this.GetObservable(MarkersProperty).Subscribe(_ =>
        {
            if (_currentTimeTextBox != null && _currentTimeTextBox.IsVisible)
            {
                UpdateMarkerPopup(_currentTimeTextBox.Text);
            }
            else
            {
                CloseMarkerPopup();
            }
        });
    }
}
