using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Controls.Behaviors;
using Beutl.Controls.Converters;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.ElementPropertyTab.Views;

public sealed partial class EngineObjectPropertyView : UserControl
{
    public EngineObjectPropertyView()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
        Interaction.SetBehaviors(this,
        [
            new _DragBehavior() { Orientation = Orientation.Vertical, DragControl = dragBorder },
        ]);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void SaveAsTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EngineObjectPropertyViewModel viewModel) return;

        string defaultName = TypeDisplayHelpers.GetLocalizedName(viewModel.Model.GetType());
        string uniqueName = ObjectTemplateService.Instance.GetUniqueName(defaultName);

        var flyout = new SaveAsTemplateFlyout { Text = uniqueName };
        flyout.Confirmed += (_, name) =>
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                ObjectTemplateService.Instance.AddFromInstance(viewModel.Model, name);
            }
        };
        flyout.ShowAt(this);
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EngineObjectPropertyViewModel viewModel2)
        {
            HistoryManager history = viewModel2.GetRequiredService<HistoryManager>();
            EngineObject obj = viewModel2.Model;
            Element element = obj.FindRequiredHierarchicalParent<Element>();
            element.RemoveObject(obj);
            history.Commit(CommandNames.RemoveObject);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } item2
            && DataContext is EngineObjectPropertyViewModel viewModel2)
        {
            HistoryManager history = viewModel2.GetRequiredService<HistoryManager>();
            EngineObject obj = viewModel2.Model;
            Element element = obj.FindRequiredHierarchicalParent<Element>();
            Rect bounds = Bounds;
            Point position = e.GetPosition(this);
            double half = bounds.Height / 2;
            int index = element.Objects.IndexOf(obj);

            if (half < position.Y)
            {
                element.InsertObject(index + 1, (EngineObject)Activator.CreateInstance(item2)!);
            }
            else
            {
                element.InsertObject(index, (EngineObject)Activator.CreateInstance(item2)!);
            }

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EngineObjectPropertyViewModel viewModel)
        {
            if (!viewModel.IsFallback.Value)
            {
                EngineObject obj = viewModel.Model;
                Type type = obj.GetType();
                headerText.Text = TypeDisplayHelpers.GetLocalizedName(type);
                ToolTip.SetTip(headerPanel, TypeDisplayHelpers.GetLocalizedDescription(type));

                if (panel.Children.Count == 2)
                {
                    panel.Children.RemoveAt(1);
                }
            }
            else
            {
                headerText.Text = Strings.Unknown;

                if (panel.Children.Count == 1)
                {
                    panel.Children.Add(new FallbackObjectView());
                }
            }
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is ElementPropertyTabViewModel
                {
                    Element.Value: { } element
                } viewModel)
            {
                HistoryManager history = viewModel.GetRequiredService<HistoryManager>();
                element.Objects.Move(oldIndex, newIndex);
                history.Commit(CommandNames.MoveObject);
            }
        }
    }

}
