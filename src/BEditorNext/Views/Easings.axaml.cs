using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BEditorNext.Views;

public partial class Easings : UserControl
{
    public Easings()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
    }

    private async void Pressed(object? sender, PointerPressedEventArgs e)
    {
        int index = 0;
        foreach (object? item in itemsControl.Items)
        {
            IControl control = itemsControl.ItemContainerGenerator.ContainerFromIndex(index);

            if (control.IsPointerOver)
            {
                var data = new DataObject();
                data.Set("Easing", item);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
                return;
            }

            index++;
        }
    }
}
