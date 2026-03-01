using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.Behaviors;
using Beutl.Editor.Components.SourceOperatorsTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.SourceOperatorsTab.Views;

public sealed partial class SourceOperatorView : UserControl
{
    public SourceOperatorView()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        Interaction.SetBehaviors(this,
        [
            new _DragBehavior() { Orientation = Orientation.Vertical, DragControl = dragBorder },
        ]);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceOperatorViewModel viewModel2)
        {
            HistoryManager history = viewModel2.GetRequiredService<HistoryManager>();
            EngineObject obj = viewModel2.Model;
            Element element = obj.FindRequiredHierarchicalParent<Element>();
            element.RemoveObject(obj);
            history.Commit(CommandNames.RemoveSourceOperator);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.EngineObject) is { } typeName
            && TypeFormat.ToType(typeName) is { } item2
            && DataContext is SourceOperatorViewModel viewModel2)
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

            history.Commit(CommandNames.AddSourceOperator);

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
        if (DataContext is SourceOperatorViewModel viewModel)
        {
            if (!viewModel.IsDummy.Value)
            {
                EngineObject obj = viewModel.Model;
                Type type = obj.GetType();
                headerText.Text = TypeDisplayHelpers.GetLocalizedName(type);

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
                    panel.Children.Add(new UnknownObjectView());
                }
            }
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (itemsControl?.DataContext is SourceOperatorsTabViewModel
                {
                    Element.Value: { } element
                } viewModel)
            {
                HistoryManager history = viewModel.GetRequiredService<HistoryManager>();
                element.Objects.Move(oldIndex, newIndex);
                history.Commit(CommandNames.MoveSourceOperator);
            }
        }
    }

    public sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPropertyEditorContext viewModel)
            {
                if (viewModel.Extension.TryCreateControl(viewModel, out var control))
                {
                    return control;
                }
                else
                {
                    return new Label
                    {
                        Height = 24,
                        Margin = new Thickness(0, 4),
                        Content = viewModel.Extension.DisplayName
                    };
                }
            }
            else
            {
                return BindingNotification.Null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
