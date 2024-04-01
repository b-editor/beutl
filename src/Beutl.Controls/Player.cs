using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Beutl.Controls;

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
    private ToggleButton _playButton;
    private RepeatButton _nextButton;
    private RepeatButton _previousButton;
    private Button _endButton;
    private Button _startButton;
    private Slider _slider;
    private ContentPresenter _innerLeftPresenter;
    private ContentPresenter _contentPresenter;
    private ICommand _playButtonCommand;
    private ICommand _nextButtonCommand;
    private ICommand _previousButtonCommand;
    private ICommand _endButtonCommand;
    private ICommand _startButtonCommand;

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

        _playButton.Click += (s, e) => PlayButtonCommand?.Execute(null);
        _nextButton.Click += (s, e) => NextButtonCommand?.Execute(null);
        _previousButton.Click += (s, e) => PreviousButtonCommand?.Execute(null);
        _endButton.Click += (s, e) => EndButtonCommand?.Execute(null);
        _startButton.Click += (s, e) => StartButtonCommand?.Execute(null);

        _innerLeftPresenter.GetObservable(BoundsProperty).Subscribe(OnInnerLeftBoundsChanged);
    }

    private void OnInnerLeftBoundsChanged(Rect rect)
    {
        _contentPresenter.Margin = new Thickness(rect.Width, 0);
    }
}
