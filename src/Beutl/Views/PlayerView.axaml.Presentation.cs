using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class PlayerView
{
    internal PresentationWindow? Presentation { get; private set; }

    private void OnPresentationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var menu = new MenuFlyout();
        if (Presentation is { } existing)
        {
            var fullscreen = new MenuItem { Header = Strings.PresentationToggleFullScreen };
            fullscreen.Click += (_, _) => existing.ToggleFullScreen();
            var close = new MenuItem { Header = Strings.PresentationClose };
            close.Click += (_, _) => existing.Close();
            menu.Items.Add(fullscreen);
            menu.Items.Add(close);
        }
        else
        {
            var windowed = new MenuItem { Header = Strings.PresentationWindowed };
            windowed.Click += (_, _) => OpenPresentation(owner.Screens.ScreenFromWindow(owner), false);
            menu.Items.Add(windowed);
            int number = 0;
            foreach (Screen screen in owner.Screens.All)
            {
                number++;
                string name = screen.DisplayName ?? string.Format(Strings.PresentationDisplay, number);
                var item = new MenuItem
                {
                    Header = $"{Strings.PresentationFullScreen}: {name} ({screen.Bounds.Width} × {screen.Bounds.Height})"
                };
                item.Click += (_, _) => OpenPresentation(screen, true);
                menu.Items.Add(item);
            }
        }
        menu.ShowAt(button);
    }

    internal void OpenPresentation(Screen? screen, bool fullscreen)
    {
        if (DataContext is not PlayerViewModel player || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        if (Presentation is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new PresentationWindow(player);
        Presentation = window;
        window.Closed += (_, _) => Presentation = null;
        window.ShowOnScreen(owner, screen, fullscreen);
    }
}
