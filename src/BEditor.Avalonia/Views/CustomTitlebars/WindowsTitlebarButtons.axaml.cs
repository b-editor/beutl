using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.CustomTitlebars
{
    public sealed class WindowsTitlebarButtons : UserControl
    {
        public static readonly StyledProperty<bool> CanResizeProperty = AvaloniaProperty.Register<WindowsTitlebarButtons, bool>(nameof(CanResize), true, notifying: (obj, value) =>
        {
            if (obj is WindowsTitlebarButtons titlebar)
            {
                titlebar._maximizeButton.IsVisible = value;
            }
        });
        private readonly Button _minimizeButton;
        private readonly Button _maximizeButton;
        private readonly Path _maximizeIcon;
        private readonly ToolTip _maximizeToolTip;
        private readonly Button _closeButton;

        public WindowsTitlebarButtons()
        {
            InitializeComponent();

            _minimizeButton = this.FindControl<Button>("MinimizeButton");
            _maximizeButton = this.FindControl<Button>("MaximizeButton");
            _maximizeIcon = this.FindControl<Path>("MaximizeIcon");
            _maximizeToolTip = this.FindControl<ToolTip>("MaximizeToolTip");
            _closeButton = this.FindControl<Button>("CloseButton");

            if (OperatingSystem.IsWindows())
            {
                _minimizeButton.Click += MinimizeWindow;
                _maximizeButton.Click += MaximizeWindow;
                _closeButton.Click += CloseWindow;

                PointerPressed += Titlebar_PointerPressed;

                SubscribeToWindowState();
            }
            else
            {
                IsVisible = false;
            }
        }

        public bool CanResize
        {
            get => GetValue(CanResizeProperty);
            set => SetValue(CanResizeProperty, value);
        }

        private void Titlebar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.BeginMoveDrag(e);
            }
        }

        private void CloseWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.Close();
            }
        }

        private void MaximizeWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                if (window.WindowState is WindowState.Normal)
                {
                    window.WindowState = WindowState.Maximized;
                }
                else
                {
                    window.WindowState = WindowState.Normal;
                }
            }
        }

        private void MinimizeWindow(object? sender, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.WindowState = WindowState.Minimized;
            }
        }

        private async void SubscribeToWindowState()
        {
            if (VisualRoot is Window window)
            {
                while (window is null)
                {
                    window = (Window)VisualRoot;
                    await Task.Delay(50);
                }

                window.GetObservable(Window.WindowStateProperty).Subscribe(s =>
                {
                    if (s is not WindowState.Maximized)
                    {
                        _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");
                        window.Padding = new Thickness(0, 0, 0, 0);
                        _maximizeToolTip.Content = "Maximize";
                    }
                    if (s is WindowState.Maximized)
                    {
                        _maximizeIcon.Data = Avalonia.Media.Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");
                        window.Padding = new Thickness(7, 7, 7, 7);
                        _maximizeToolTip.Content = "Restore Down";
                    }
                });
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}