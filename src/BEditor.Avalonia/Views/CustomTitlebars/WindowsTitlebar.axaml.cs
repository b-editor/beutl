using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;

namespace BEditor.Views.CustomTitlebars
{
    public class WindowsTitlebar : UserControl
    {
        private readonly Button _minimizeButton;
        private readonly Button _maximizeButton;
        private readonly Path _maximizeIcon;
        private readonly ToolTip _maximizeToolTip;
        private readonly Button _closeButton;
        private readonly Menu _menu;
        private readonly StackPanel _titlebarbuttons;

        public WindowsTitlebar()
        {
            InitializeComponent();
            _minimizeButton = this.FindControl<Button>("MinimizeButton");
            _maximizeButton = this.FindControl<Button>("MaximizeButton");
            _maximizeIcon = this.FindControl<Path>("MaximizeIcon");
            _maximizeToolTip = this.FindControl<ToolTip>("MaximizeToolTip");
            _closeButton = this.FindControl<Button>("CloseButton");
            _menu = this.FindControl<Menu>("menu");
            _titlebarbuttons = this.FindControl<StackPanel>("titlebarbuttons");

            if (OperatingSystem.IsWindows())
            {
                _minimizeButton.Click += MinimizeWindow;
                _maximizeButton.Click += MaximizeWindow;
                _closeButton.Click += CloseWindow;

                PointerPressed += WindowsTitlebar_PointerPressed;

                _menu.MenuOpened += (s, e) => PointerPressed -= WindowsTitlebar_PointerPressed;
                _menu.MenuClosed += (s, e) => PointerPressed += WindowsTitlebar_PointerPressed;

                SubscribeToWindowState();
            }
            else if (OperatingSystem.IsLinux())
            {
                _titlebarbuttons.IsVisible = false;
            }
            else if (OperatingSystem.IsMacOS())
            {
                IsVisible = false;
            }
        }

        public async void ShowSettings(object s, RoutedEventArgs e)
        {
            await new SettingsWindow().ShowDialog((Window)VisualRoot);
        }

        public async void CreateProjectClick(object s, RoutedEventArgs e)
        {
            var viewmodel = new CreateProjectViewModel();
            var content = new CreateProject
            {
                DataContext = viewmodel
            };
            var dialog = new EmptyDialog(content);

            await dialog.ShowDialog((Window)VisualRoot);
        }

        public void WindowsTitlebar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var hostWindow = (Window)VisualRoot;
            hostWindow.BeginMoveDrag(e);
        }

        private void CloseWindow(object? sender, RoutedEventArgs e)
        {
            var hostWindow = (Window)VisualRoot;
            hostWindow.Close();
        }

        private void MaximizeWindow(object? sender, RoutedEventArgs e)
        {
            var hostWindow = (Window)VisualRoot;

            if (hostWindow.WindowState is WindowState.Normal)
            {
                hostWindow.WindowState = WindowState.Maximized;
            }
            else
            {
                hostWindow.WindowState = WindowState.Normal;
            }
        }

        private void MinimizeWindow(object? sender, RoutedEventArgs e)
        {
            var hostWindow = (Window)VisualRoot;
            hostWindow.WindowState = WindowState.Minimized;
        }

        private async void SubscribeToWindowState()
        {
            var hostWindow = (Window)VisualRoot;

            while (hostWindow is null)
            {
                hostWindow = (Window)VisualRoot;
                await Task.Delay(50);
            }

            hostWindow.GetObservable(Window.WindowStateProperty).Subscribe(s =>
            {
                if (s is not WindowState.Maximized)
                {
                    _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");
                    hostWindow.Padding = new Thickness(0, 0, 0, 0);
                    _maximizeToolTip.Content = "Maximize";
                }
                if (s is WindowState.Maximized)
                {
                    _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");
                    hostWindow.Padding = new Thickness(7, 7, 7, 7);
                    _maximizeToolTip.Content = "Restore Down";

                    // This should be a more universal approach in both cases, but I found it to be less reliable, when for example double-clicking the title bar.
                    /*hostWindow.Padding = new Thickness(
                            hostWindow.OffScreenMargin.Left,
                            hostWindow.OffScreenMargin.Top,
                            hostWindow.OffScreenMargin.Right,
                            hostWindow.OffScreenMargin.Bottom);*/
                }
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
