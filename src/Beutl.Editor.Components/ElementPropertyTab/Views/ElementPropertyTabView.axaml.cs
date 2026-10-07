using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.ElementPropertyTab.Views;

public sealed partial class ElementPropertyTabView : UserControl
{
    public ElementPropertyTabView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
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
