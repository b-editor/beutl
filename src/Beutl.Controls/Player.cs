#nullable enable
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Controls;

public sealed class TimecodeSubmittedEventArgs : EventArgs
{
    public TimecodeSubmittedEventArgs(string input)
    {
        Input = input ?? string.Empty;
    }

    public string Input { get; }

    public bool Handled { get; set; }

    public string? Error { get; set; }
}
#nullable restore

public class Player : RangeBase
{
    public static readonly StyledProperty<string> DurationProperty = AvaloniaProperty.Register<Player, string>(nameof(Duration));
    public static readonly StyledProperty<object> ContentProperty = AvaloniaProperty.Register<Player, object>(nameof(InnerLeftContent));
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

    private string _currentTime = string.Empty;
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

    public void SetSeekBarOpacity(double opacity)
    {
        if (_slider != null)
        {
            _slider.Opacity = opacity;
        }
    }

    public void BeginEditCurrentTime()
    {
        if (_currentTimeTextBox == null) return;

        _currentTimeTextBox.Classes.Remove("invalid");
        ToolTip.SetTip(_currentTimeTextBox, null);
        _currentTimeTextBox.Text = _currentTime;
        _currentTimeTextBox.IsVisible = true;
        if (_currentTimeTextBlock != null)
        {
            _currentTimeTextBlock.IsVisible = false;
        }
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
    }

    private void SubmitCurrentTimeEdit()
    {
        if (_currentTimeTextBox == null) return;
        string input = _currentTimeTextBox.Text ?? string.Empty;
        var handler = CurrentTimeSubmitted;

        TimecodeSubmittedEventArgs args = new TimecodeSubmittedEventArgs(input);
        handler?.Invoke(this, args);

        if (args.Handled)
        {
            EndEditCurrentTime();
            return;
        }

        _currentTimeTextBox.Classes.Add("invalid");
        ToolTip.SetTip(_currentTimeTextBox, !string.IsNullOrEmpty(args.Error) ? args.Error : "Cannot process timecode submission.");
        _currentTimeTextBox.SelectAll();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
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
            _currentTimeTextBox.GetObservable(TextBox.TextProperty).Subscribe(_ =>
            {
                _currentTimeTextBox.Classes.Remove("invalid");
                ToolTip.SetTip(_currentTimeTextBox, null);
            });
        }

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
