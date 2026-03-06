using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ElementPropertyTabViewModel viewModel)
        {
            var self = new WeakReference<ElementPropertyTabView>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out ElementPropertyTabView? @this) && @this.DataContext is ElementPropertyTabViewModel viewModel)
                {
                    int index = 0;
                    bool found = false;
                    for (; index < viewModel.Items.Count; index++)
                    {
                        EngineObjectPropertyViewModel? item = viewModel.Items[index];
                        if (ReferenceEquals(item?.Model, obj))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        Control? ctrl = @this.itemsControl.ContainerFromIndex(index);
                        if (ctrl != null)
                        {
                            @this.scrollViewer.Offset = new Avalonia.Vector(@this.scrollViewer.Offset.X, ctrl.Bounds.Top);
                        }
                    }
                }
            };
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } item
            && DataContext is ElementPropertyTabViewModel vm
            && vm.Element.Value is Element element)
        {
            HistoryManager history = vm.GetRequiredService<HistoryManager>();
            element.AddObject((EngineObject)Activator.CreateInstance(item)!);
            history.Commit(CommandNames.AddObject);

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.EngineObject))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
    }
}
