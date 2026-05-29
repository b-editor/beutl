#nullable enable
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging;

namespace Beutl.Controls;

// nullable is enabled per-type because the surrounding Player class predates
// nullable annotations; enabling at file scope would force a broad rewrite of
// its private fields.
/// <summary>
/// Event args for <see cref="Player.CurrentTimeSubmitted"/>. Subscribers must call
/// <see cref="Accept"/> to consume the submission (closes the editor) or
/// <see cref="Reject"/> with a localized message to keep the editor open and
/// display the message as a validation tooltip.
/// </summary>
public sealed class TimecodeSubmittedEventArgs : EventArgs
{
    public TimecodeSubmittedEventArgs(string input)
    {
        Input = input ?? string.Empty;
    }

    public string Input { get; }

    public bool Handled { get; private set; }

    public string? Error { get; private set; }

    public void Accept()
    {
        Handled = true;
        Error = null;
    }

    public void Reject(string error)
    {
        Handled = false;
        Error = error ?? string.Empty;
    }
}
#nullable restore

public class Player : RangeBase
{
    public static readonly StyledProperty<string> DurationProperty = AvaloniaProperty.Register<Player, string>(nameof(Duration));
    public static readonly StyledProperty<object> ContentProperty = AvaloniaProperty.Register<Player, object>(nameof(Content));
    public static readonly StyledProperty<object> InnerLeftContentProperty = AvaloniaProperty.Register<Player, object>(nameof(InnerLeftContent));
    public static readonly StyledProperty<object> InnerRightContentProperty = AvaloniaProperty.Register<Player, object>(nameof(InnerRightContent));
    public static readonly DirectProperty<Player, string> CurrentTimeProperty =
        AvaloniaProperty.RegisterDirect<Player, string>(
            nameof(CurrentTime),
            owner => owner.CurrentTime,
            (owner, obj) => owner.CurrentTime = obj);
    public static readonly DirectProperty<Player, bool> IsPlayingProperty =
        AvaloniaProperty.RegisterDirect<Player, bool>(
            nameof(IsPlaying),
            owner => owner.IsPlaying,
            (owner, obj) => owner.IsPlaying = obj);

    public static readonly DirectProperty<Player, bool> IsLoopEnabledProperty =
        AvaloniaProperty.RegisterDirect<Player, bool>(
            nameof(IsLoopEnabled),
            owner => owner.IsLoopEnabled,
            (owner, obj) => owner.IsLoopEnabled = obj,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly DirectProperty<Player, ICommand> PlayButtonCommandProperty =
        AvaloniaProperty.RegisterDirect<Player, ICommand>(
            nameof(PlayButtonCommand),
            owner => owner.PlayButtonCommand,
            (owner, obj) => owner.PlayButtonCommand = obj);

    public static readonly DirectProperty<Player, ICommand> NextButtonCommandProperty =
        AvaloniaProperty.RegisterDirect<Player, ICommand>(
            nameof(NextButtonCommand),
            owner => owner.NextButtonCommand,
            (owner, obj) => owner.NextButtonCommand = obj);

    public static readonly DirectProperty<Player, ICommand> PreviousButtonCommandProperty =
        AvaloniaProperty.RegisterDirect<Player, ICommand>(
            nameof(PreviousButtonCommand),
            owner => owner.PreviousButtonCommand,
            (owner, obj) => owner.PreviousButtonCommand = obj);

    public static readonly DirectProperty<Player, ICommand> EndButtonCommandProperty =
        AvaloniaProperty.RegisterDirect<Player, ICommand>(
            nameof(EndButtonCommand),
            owner => owner.EndButtonCommand,
            (owner, obj) => owner.EndButtonCommand = obj);

    public static readonly DirectProperty<Player, ICommand> StartButtonCommandProperty =
        AvaloniaProperty.RegisterDirect<Player, ICommand>(
            nameof(StartButtonCommand),
            owner => owner.StartButtonCommand,
            (owner, obj) => owner.StartButtonCommand = obj);

    public static readonly StyledProperty<IReadOnlyList<PlayerMarkerEntry>> MarkersProperty =
        AvaloniaProperty.Register<Player, IReadOnlyList<PlayerMarkerEntry>>(nameof(Markers));

    private static readonly ILogger s_logger = Beutl.Logging.Log.CreateLogger<Player>();

    private string _currentTime = string.Empty;
    private string _editStartText = string.Empty;
    private bool _isPlaying;
    private bool _isLoopEnabled;
    private ToggleButton _playButton;
    private RepeatButton _nextButton;
    private RepeatButton _previousButton;
    private Button _endButton;
    private Button _startButton;
    private Slider _slider;
    private ContentPresenter _innerLeftPresenter;
    private ContentPresenter _contentPresenter;
    private TextBlock _currentTimeTextBlock;
    private TextBox _currentTimeTextBox;
    private IDisposable _currentTimeTextBoxTextSubscription;
    private Popup _markerPopup;
    private ListBox _markerListBox;
    private IDisposable _markersChangeSubscription;
    private ICommand _playButtonCommand;
    private ICommand _nextButtonCommand;
    private ICommand _previousButtonCommand;
    private ICommand _endButtonCommand;
    private ICommand _startButtonCommand;

    public event EventHandler<TimecodeSubmittedEventArgs> CurrentTimeSubmitted;

    public string Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public object InnerLeftContent
    {
        get => GetValue(InnerLeftContentProperty);
        set => SetValue(InnerLeftContentProperty, value);
    }

    public object InnerRightContent
    {
        get => GetValue(InnerRightContentProperty);
        set => SetValue(InnerRightContentProperty, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        set => SetAndRaise(CurrentTimeProperty, ref _currentTime, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetAndRaise(IsPlayingProperty, ref _isPlaying, value);
    }

    public bool IsLoopEnabled
    {
        get => _isLoopEnabled;
        set => SetAndRaise(IsLoopEnabledProperty, ref _isLoopEnabled, value);
    }

    public ICommand PlayButtonCommand
    {
        get => _playButtonCommand;
        set => SetAndRaise(PlayButtonCommandProperty, ref _playButtonCommand, value);
    }

    public ICommand NextButtonCommand
    {
        get => _nextButtonCommand;
        set => SetAndRaise(NextButtonCommandProperty, ref _nextButtonCommand, value);
    }

    public ICommand PreviousButtonCommand
    {
        get => _previousButtonCommand;
        set => SetAndRaise(PreviousButtonCommandProperty, ref _previousButtonCommand, value);
    }

    public ICommand EndButtonCommand
    {
        get => _endButtonCommand;
        set => SetAndRaise(EndButtonCommandProperty, ref _endButtonCommand, value);
    }

    public ICommand StartButtonCommand
    {
        get => _startButtonCommand;
        set => SetAndRaise(StartButtonCommandProperty, ref _startButtonCommand, value);
    }

    public IReadOnlyList<PlayerMarkerEntry> Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public void SetSeekBarOpacity(double opacity)
    {
        if (_slider != null)
        {
            _slider.Opacity = opacity;
        }
    }

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

    private void UpdateMarkerPopup(string text)
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
        IReadOnlyList<PlayerMarkerEntry> source = Markers;
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
        if (_markerListBox == null || _currentTimeTextBox == null) return;
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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

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

        _playButton = e.NameScope.Find<ToggleButton>("PART_PlayButton");
        _nextButton = e.NameScope.Find<RepeatButton>("PART_NextButton");
        _previousButton = e.NameScope.Find<RepeatButton>("PART_PreviousButton");
        _endButton = e.NameScope.Find<Button>("PART_EndButton");
        _startButton = e.NameScope.Find<Button>("PART_StartButton");
        _slider = e.NameScope.Find<Slider>("PART_Slider");
        _innerLeftPresenter = e.NameScope.Find<ContentPresenter>("InnerLeftPresenter");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("ContentPresenter");
        _currentTimeTextBlock = e.NameScope.Find<TextBlock>("PART_CurrentTimeTextBlock");
        _currentTimeTextBox = e.NameScope.Find<TextBox>("PART_CurrentTimeTextBox");
        _markerPopup = e.NameScope.Find<Popup>("PART_MarkerPopup");
        _markerListBox = e.NameScope.Find<ListBox>("PART_MarkerListBox");

        _playButton.Click += (s, e) => PlayButtonCommand?.Execute(null);
        _nextButton.Click += (s, e) => NextButtonCommand?.Execute(null);
        _previousButton.Click += (s, e) => PreviousButtonCommand?.Execute(null);
        _endButton.Click += (s, e) => EndButtonCommand?.Execute(null);
        _startButton.Click += (s, e) => StartButtonCommand?.Execute(null);

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

        _innerLeftPresenter.GetObservable(BoundsProperty).Subscribe(OnInnerLeftBoundsChanged);
    }

    private void OnCurrentTimeTextBlockPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_currentTimeTextBlock).Properties.IsLeftButtonPressed)
        {
            BeginEditCurrentTime();
            e.Handled = true;
        }
    }

    private void OnCurrentTimeTextBoxKeyDown(object sender, KeyEventArgs e)
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

    private void OnMarkerListBoxPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is not Visual source) return;

        // 起点アイテム経由で取得することで、SelectedItem 反映前のクリックでも誤確定しない。
        ListBoxItem item = FindListBoxItem(source);
        if (item == null || item.DataContext is not PlayerMarkerEntry entry) return;

        _markerListBox.SelectedItem = entry;
        e.Handled = true;
        CommitMarkerSelection();
    }

    private static ListBoxItem FindListBoxItem(Visual source)
    {
        Visual current = source;
        while (current != null)
        {
            if (current is ListBoxItem item) return item;
            current = current.GetVisualParent();
        }
        return null;
    }

    private void OnCurrentTimeTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_currentTimeTextBox != null && _currentTimeTextBox.IsVisible)
        {
            EndEditCurrentTime();
        }
    }

    private void OnInnerLeftBoundsChanged(Rect rect)
    {
        _contentPresenter.Margin = new Thickness(rect.Width, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayButtonCommandProperty)
        {
            if (change.OldValue is ICommand oldValue)
            {
                oldValue.CanExecuteChanged -= OnPlayButtonCommandCanExecuteChanged;
                if (_playButton != null)
                    _playButton.IsEnabled = false;
            }

            if (change.NewValue is ICommand newValue)
            {
                newValue.CanExecuteChanged += OnPlayButtonCommandCanExecuteChanged;
                if (_playButton != null)
                    _playButton.IsEnabled = newValue.CanExecute(null);
            }
        }
    }

    private void OnPlayButtonCommandCanExecuteChanged(object sender, EventArgs e)
    {
        if (_playButton != null && PlayButtonCommand != null)
        {
            _playButton.IsEnabled = PlayButtonCommand.CanExecute(null);
        }
    }
}
