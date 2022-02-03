using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Input;

using FluentAvalonia.Core.ApplicationModel;
using FluentAvalonia.UI.Controls;

namespace BeUtl.Views;

public sealed partial class MainWindow : CoreWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // タイトルバーの設定
        if (OperatingSystem.IsWindows())
        {
            ((ICoreApplicationView)this).TitleBar.ExtendViewIntoTitleBar = true;
            SetTitleBar(mainView.Titlebar);
            mainView.TitleBarArea.PointerPressed += TitleBarArea_PointerPressed;
        }

        NotificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight
        };
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public WindowNotificationManager NotificationManager { get; }

    private void TitleBarArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
