using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.Configuration;

namespace Beutl.Controls;

[PseudoClasses(ShuttlingClass, CompactClass, TinyClass, EditingClass)]
public partial class Player : RangeBase
{
    private const string ShuttlingClass = ":shuttling";
    private const string CompactClass = ":compact";
    private const string TinyClass = ":tiny";

    // While the current time is being edited, in a box wider than the time it replaces.
    private const string EditingClass = ":editing";

    // Below this width the total time would push the footer onto a second row.
    private const double CompactWidth = 530;

    // Below this width the footer's sides do not fit on a row of their own.
    private const double TinyWidth = 216;

    public static readonly DirectProperty<Player, string> CurrentTimeProperty =
        AvaloniaProperty.RegisterDirect<Player, string>(
            nameof(CurrentTime),
            owner => owner.CurrentTime,
            (owner, obj) => owner.CurrentTime = obj);
    private string _currentTime = string.Empty;
    private string _editStartText = string.Empty;
    private ToggleButton? _playButton;
    private RepeatButton? _nextButton;
    private RepeatButton? _previousButton;
    private Button? _endButton;
    private Button? _startButton;
    private ContentPresenter? _innerLeftPresenter;
    private ContentPresenter? _contentPresenter;
    private TextBlock? _currentTimeTextBlock;
    private TextBox? _currentTimeTextBox;
    private IDisposable? _currentTimeTextBoxTextSubscription;
    private Popup? _markerPopup;
    private ListBox? _markerListBox;
    private IDisposable? _markersChangeSubscription;
    public event EventHandler<TimecodeSubmittedEventArgs>? CurrentTimeSubmitted;

    [GeneratedStyledProperty]
    public partial string? Duration { get; set; }

    [GeneratedStyledProperty]
    public partial object? Content { get; set; }

    [GeneratedStyledProperty]
    public partial object? InnerLeftContent { get; set; }

    [GeneratedStyledProperty]
    public partial object? InnerRightContent { get; set; }

    /// <summary>Gets or sets the controls at the trailing end of the footer, beside the transport buttons.</summary>
    [GeneratedStyledProperty]
    public partial object? FooterRightContent { get; set; }

    /// <summary>Gets or sets the shuttle's rate, such as 2×, shown beside the time. Null while there is none to show.</summary>
    [GeneratedStyledProperty]
    public partial string? PlaybackRateText { get; set; }

    public string CurrentTime
    {
        get => _currentTime;
        set => SetAndRaise(CurrentTimeProperty, ref _currentTime, value);
    }

    [GeneratedDirectProperty]
    public partial bool IsPlaying { get; set; }

    [GeneratedDirectProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial bool IsLoopEnabled { get; set; }

    [GeneratedDirectProperty]
    public partial ICommand? PlayButtonCommand { get; set; }

    [GeneratedDirectProperty]
    public partial ICommand? NextButtonCommand { get; set; }

    [GeneratedDirectProperty]
    public partial ICommand? PreviousButtonCommand { get; set; }

    [GeneratedDirectProperty]
    public partial ICommand? EndButtonCommand { get; set; }

    [GeneratedDirectProperty]
    public partial ICommand? StartButtonCommand { get; set; }

    /// <summary>Gets or sets the markers, which the seek bar marks and the time editor offers after an @.</summary>
    [GeneratedStyledProperty]
    public partial IReadOnlyList<PlayerMarkerEntry>? Markers { get; set; }

    /// <summary>Gets or sets the rate that the seek bar's tip counts frames at. It must be positive.</summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(PlayerSeekBar))]
    public partial Rational FrameRate { get; set; }

    /// <summary>
    /// Gets or sets how the seek bar's tip writes times. <see cref="CurrentTime"/> and <see cref="Duration"/> come
    /// written, and should be written the same way.
    /// </summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(PlayerSeekBar))]
    public partial TimeDisplayFormat TimeDisplayFormat { get; set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        DetachCurrentTimeEditor();

        _playButton = e.NameScope.Find<ToggleButton>("PART_PlayButton");
        _nextButton = e.NameScope.Find<RepeatButton>("PART_NextButton");
        _previousButton = e.NameScope.Find<RepeatButton>("PART_PreviousButton");
        _endButton = e.NameScope.Find<Button>("PART_EndButton");
        _startButton = e.NameScope.Find<Button>("PART_StartButton");
        _innerLeftPresenter = e.NameScope.Find<ContentPresenter>("InnerLeftPresenter");
        _contentPresenter = e.NameScope.Find<ContentPresenter>("ContentPresenter");
        _currentTimeTextBlock = e.NameScope.Find<TextBlock>("PART_CurrentTimeTextBlock");
        _currentTimeTextBox = e.NameScope.Find<TextBox>("PART_CurrentTimeTextBox");
        _markerPopup = e.NameScope.Find<Popup>("PART_MarkerPopup");
        _markerListBox = e.NameScope.Find<ListBox>("PART_MarkerListBox");

        if (_playButton != null)
            _playButton.Click += (s, e) => PlayButtonCommand?.Execute(null);
        if (_nextButton != null)
            _nextButton.Click += (s, e) => NextButtonCommand?.Execute(null);
        if (_previousButton != null)
            _previousButton.Click += (s, e) => PreviousButtonCommand?.Execute(null);
        if (_endButton != null)
            _endButton.Click += (s, e) => EndButtonCommand?.Execute(null);
        if (_startButton != null)
            _startButton.Click += (s, e) => StartButtonCommand?.Execute(null);

        AttachCurrentTimeEditor();

        _innerLeftPresenter?.GetObservable(BoundsProperty).Subscribe(OnInnerLeftBoundsChanged);
    }

    private void OnInnerLeftBoundsChanged(Rect rect)
    {
        if (_contentPresenter != null)
            _contentPresenter.Margin = new Thickness(rect.Width, 0);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PseudoClasses.Set(CompactClass, e.NewSize.Width < CompactWidth);
        PseudoClasses.Set(TinyClass, e.NewSize.Width < TinyWidth);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlaybackRateTextProperty)
        {
            PseudoClasses.Set(ShuttlingClass, !string.IsNullOrEmpty(change.GetNewValue<string?>()));
        }
        else if (change.Property == PlayButtonCommandProperty)
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

    private void OnPlayButtonCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        if (_playButton != null && PlayButtonCommand != null)
        {
            _playButton.IsEnabled = PlayButtonCommand.CanExecute(null);
        }
    }
}
