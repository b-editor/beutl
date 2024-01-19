using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
        var data = new DataObject();
        data.Set(KnownLibraryItemFormats.Easing, new Animation.Easings.SplineEasing());
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
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
                    var data = new DataObject();
                    data.Set(KnownLibraryItemFormats.Easing, item);
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
                    return;
                }
            }
        }
    }
}
