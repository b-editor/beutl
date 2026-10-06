using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.PackageTools.UI.Views;

internal static class TaskDialogButtons
{
    public static FATaskDialogButtonsPanel CreatePanel()
    {
        return new FATaskDialogButtonsPanel
        {
            [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Continue,
            Spacing = 8
        };
    }

    public static Control CreatePanel(string content, EventHandler<RoutedEventArgs> click)
    {
        FATaskDialogButtonsPanel panel = CreatePanel();
        var button = new FATaskDialogButtonHost()
        {
            Content = content
        };
        button.Click += click;
        panel.Children.Add(button);

        return panel;
    }

    public static Control CreateBackPanel(Visual page)
    {
        return CreatePanel(Strings.Back, (s, e) =>
        {
            FAFrame? frame = page.FindAncestorOfType<FAFrame>();
            frame?.GoBack();
        });
    }
}
