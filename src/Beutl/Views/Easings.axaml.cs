using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Views;

public sealed partial class Easings : UserControl
{
    public Easings()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
    }

    private async void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (itemsControl.Items is { } items)
        {
            int index = 0;
            foreach (object? item in items)
            {
                IControl? control = itemsControl.ItemContainerGenerator.ContainerFromIndex(index);

                if (control?.IsPointerOver == true)
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
}
