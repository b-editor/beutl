using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.ElementPropertyTab.Views;

public sealed partial class ElementPropertyTabView : UserControl
{
    private IDisposable? _revealSubscription;

    public ElementPropertyTabView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _revealSubscription?.Dispose();
        // Selecting a new element rebuilds the items and expanding one relays out its editors, so
        // look for the containers once that layout pass has run.
        _revealSubscription = (DataContext as ElementPropertyTabViewModel)?.RevealRequested
            .Subscribe(request => Dispatcher.UIThread.Post(
                () => Reveal(request.Item, request.Property), DispatcherPriority.Background));
    }

    private void Reveal(EngineObjectPropertyViewModel item, IPropertyEditorContext? property)
    {
        if (itemsControl.ContainerFromItem(item) is not { } container)
            return;

        Control target = container;
        if (property is not null
            && container.GetVisualDescendants().OfType<EngineObjectPropertyView>().FirstOrDefault()
                ?.propertiesList.ContainerFromItem(property) is { } row)
        {
            target = row;
        }

        target.BringIntoView();
        EditFlash.Run(target);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } item
            && DataContext is ElementPropertyTabViewModel { CanEdit.Value: true } vm
            && vm.Element.Value is Element element)
        {
            vm.GetRequiredService<IElementObjectService>()
                .Add(element, (EngineObject)Activator.CreateInstance(item)!);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is ElementPropertyTabViewModel { CanEdit.Value: true }
            && e.DataTransfer.Contains(BeutlDataFormats.EngineObject))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
    }
}
