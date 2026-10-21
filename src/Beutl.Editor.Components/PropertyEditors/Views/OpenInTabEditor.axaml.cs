using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentIcons.Common;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// A property row whose button opens the property's own tool tab.
public partial class OpenInTabEditor : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<OpenInTabEditor, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public OpenInTabEditor()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public Icon Icon
    {
        get => IconElement.Icon;
        set => IconElement.Icon = value;
    }

    public string? Text
    {
        get => TextElement.Text;
        set => TextElement.Text = value;
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }
}
