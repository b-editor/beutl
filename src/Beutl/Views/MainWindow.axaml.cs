
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.ViewModels;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Media;
using FluentAvalonia.UI.Windowing;

namespace Beutl.Views;

public sealed partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        InitializeComponent();

        NotificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            Margin = new Thickness(64, 40, 0, 0)
        };
        TitleBar.Height = 40;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public WindowNotificationManager NotificationManager { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        mainView.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
