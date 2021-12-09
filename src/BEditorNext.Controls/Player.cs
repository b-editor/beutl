using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace BEditorNext.Controls;

public class Player : RangeBase
{
    public static readonly StyledProperty<string> DurationProperty = AvaloniaProperty.Register<Player, string>(nameof(Duration));
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
    public static readonly RoutedEvent PlayButtonClickEvent = RoutedEvent.Register<Player, RoutedEventArgs>(nameof(PlayButtonClick), RoutingStrategies.Bubble);
    public static readonly RoutedEvent NextButtonClickEvent = RoutedEvent.Register<Player, RoutedEventArgs>(nameof(NextButtonClickEvent), RoutingStrategies.Bubble);
    public static readonly RoutedEvent PreviousButtonClickEvent = RoutedEvent.Register<Player, RoutedEventArgs>(nameof(PreviousButtonClick), RoutingStrategies.Bubble);
    public static readonly RoutedEvent EndButtonClickEvent = RoutedEvent.Register<Player, RoutedEventArgs>(nameof(EndButtonClick), RoutingStrategies.Bubble);
    public static readonly RoutedEvent StartButtonClickEvent = RoutedEvent.Register<Player, RoutedEventArgs>(nameof(StartButtonClick), RoutingStrategies.Bubble);
    private string _currentTime = string.Empty;
    private bool _isPlaying;
    private ToggleButton _playButton;
    private Button _nextButton;
    private Button _previousButton;
    private Button _endButton;
    private Button _startButton;
    private Image _image;

    public string Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
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

    public event EventHandler<RoutedEventArgs> PlayButtonClick
    {
        add => AddHandler(PlayButtonClickEvent, value);
        remove => RemoveHandler(PlayButtonClickEvent, value);
    }

    public event EventHandler<RoutedEventArgs> NextButtonClick
    {
        add => AddHandler(NextButtonClickEvent, value);
        remove => RemoveHandler(NextButtonClickEvent, value);
    }

    public event EventHandler<RoutedEventArgs> PreviousButtonClick
    {
        add => AddHandler(PreviousButtonClickEvent, value);
        remove => RemoveHandler(PreviousButtonClickEvent, value);
    }

    public event EventHandler<RoutedEventArgs> EndButtonClick
    {
        add => AddHandler(EndButtonClickEvent, value);
        remove => RemoveHandler(EndButtonClickEvent, value);
    }

    public event EventHandler<RoutedEventArgs> StartButtonClick
    {
        add => AddHandler(StartButtonClickEvent, value);
        remove => RemoveHandler(StartButtonClickEvent, value);
    }

    public Image GetImage()
    {
        return _image;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _playButton = e.NameScope.Find<ToggleButton>("PART_PlayButton");
        _nextButton = e.NameScope.Find<Button>("PART_NextButton");
        _previousButton = e.NameScope.Find<Button>("PART_PreviousButton");
        _endButton = e.NameScope.Find<Button>("PART_EndButton");
        _startButton = e.NameScope.Find<Button>("PART_StartButton");
        _image = e.NameScope.Find<Image>("PART_Image");

        _playButton.Click += (s, e) => RaiseEvent(new RoutedEventArgs(PlayButtonClickEvent, _playButton));
        _nextButton.Click += (s, e) => RaiseEvent(new RoutedEventArgs(NextButtonClickEvent, _nextButton));
        _previousButton.Click += (s, e) => RaiseEvent(new RoutedEventArgs(PreviousButtonClickEvent, _previousButton));
        _endButton.Click += (s, e) => RaiseEvent(new RoutedEventArgs(EndButtonClickEvent, _endButton));
        _startButton.Click += (s, e) => RaiseEvent(new RoutedEventArgs(StartButtonClickEvent, _startButton));
    }
}
