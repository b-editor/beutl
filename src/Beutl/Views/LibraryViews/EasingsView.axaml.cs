using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Models;
using Beutl.Services;

namespace Beutl.Views.LibraryViews;

public partial class EasingsView : UserControl
{
    public EasingsView()
    {
        InitializeComponent();

        itemsControl.AddHandler(PointerPressedEvent, OnEasingsPointerPressed, RoutingStrategies.Tunnel);
        splineEasing.AddHandler(PointerPressedEvent, OnSplineEasingPointerPressed, RoutingStrategies.Tunnel);
    }

    private async void OnSplineEasingPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(BeutlDataFormats.Easing, TypeFormat.ToString(typeof(Animation.Easings.SplineEasing))));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy | DragDropEffects.Link);
    }

    private async void OnEasingsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (itemsControl.ItemsSource is { } items)
        {
            foreach (object? item in items)
            {
                Control? control = itemsControl.ContainerFromItem(item);

                if (control?.IsPointerOver == true)
                {
                    var data = new DataTransfer();
                    data.Add(DataTransferItem.Create(BeutlDataFormats.Easing, TypeFormat.ToString(item.GetType())));
                    await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy | DragDropEffects.Link);
                    return;
                }
            }
        }
    }
}
