using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Logging;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using BtlSize = Beutl.Graphics.Size;

namespace Beutl.Views;

internal sealed class PresentationWindow : Window
{
    private readonly PlayerViewModel _player;
    private readonly PreviewSurface _surface;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly DispatcherTimer _cursorTimer;
    private Window? _editorWindow;
    private Screen? _screen;
    private bool _started;
    private bool _closed;

    public PresentationWindow(PlayerViewModel player)
    {
        _player = player;
        Title = Strings.PresentationWindow;
        Width = 960;
        Height = 540;
        Background = Brushes.Black;
        Content = _surface = new PreviewSurface(player);
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorTimer.Stop();
            Cursor = new Cursor(StandardCursorType.None);
        };
        ScalingChanged += (_, _) => UpdateSurfaceSize();
    }

    internal void ShowOnScreen(Window owner, Screen? screen, bool fullscreen)
    {
        _screen = screen;
        if (screen is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling);
            Height = Math.Min(Height, screen.WorkingArea.Height / screen.Scaling);
            Position = screen.WorkingArea.CenterRect(new PixelRect(
                default, PixelSize.FromSize(new Size(Width, Height), screen.Scaling))).Position;
        }
        // A native owner makes this a fullscreen-auxiliary window on macOS. Keep the output
        // independent so it can enter fullscreen, and link only its lifetime to the editor.
        _editorWindow = owner;
        owner.Closed += OnEditorWindowClosed;
        // Set X11's initial fullscreen hint before the window manager maps and sizes the window.
        if (fullscreen && OperatingSystem.IsLinux())
            WindowState = WindowState.FullScreen;
        Show();
        if (fullscreen && !_closed && !OperatingSystem.IsLinux())
            WindowState = WindowState.FullScreen;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var selected = _player.EditViewModel.EditorService.IsSelectedEditor(_player.EditViewModel)
            .ToReadOnlyReactivePropertySlim();
        if (!selected.Value)
        {
            selected.Dispose();
            Close();
            return;
        }

        _started = true;
        _subscriptions.Add(_player.AcquireCleanPreview());
        _subscriptions.Add(selected);
        _subscriptions.Add(selected.Where(value => !value).Subscribe(_ => Close()));
        Screens.Changed += OnScreensChanged;
        UpdateSurfaceSize();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        if (_editorWindow is { } editorWindow)
        {
            editorWindow.Closed -= OnEditorWindowClosed;
            _editorWindow = null;
        }
        _cursorTimer.Stop();
        Screens.Changed -= OnScreensChanged;
        _subscriptions.Dispose();
        _surface.Dispose();
        if (_started)
        {
            _player.SetPresentationFrameSize(default);
        }
        base.OnClosed(e);
    }

    private void OnEditorWindowClosed(object? sender, EventArgs e) => Close();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateSurfaceSize();
    }

    private void UpdateSurfaceSize()
    {
        if (_started && !_closed)
            _player.SetPresentationFrameSize(new BtlSize(
                (float)(ClientSize.Width * RenderScaling), (float)(ClientSize.Height * RenderScaling)));
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (_screen is not null && !Screens.All.Contains(_screen))
        {
            WindowState = WindowState.Normal;
            _screen = Screens.Primary;
            if (_screen is not null)
                Position = _screen.WorkingArea.Position;
            WindowPlacement.FitToWorkingArea(this);
        }
        UpdateSurfaceSize();
    }

    internal void ToggleFullScreen()
        => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = Cursor.Default;
        _cursorTimer.Stop();
        _cursorTimer.Start();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.F11)
            ToggleFullScreen();
        else if (e.Key is not (Key.Space or Key.Left or Key.Right))
            return;
        e.Handled = true;

        try
        {
            if (e.Key == Key.Space)
                await _player.PlayPause.ExecuteAsync();
            else if (e.Key is Key.Left or Key.Right)
            {
                await _player.Pause();
                // Pause may yield while the user switches tabs or closes this window.
                if (!_closed)
                {
                    if (e.Key == Key.Left) _player.Previous.Execute();
                    else _player.Next.Execute();
                }
            }
        }
        catch (Exception ex)
        {
            Log.CreateLogger<PresentationWindow>().LogError(ex, "Presentation playback operation failed.");
        }
    }
}
