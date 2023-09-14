using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Xaml.Interactivity;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ListItemEditor : UserControl
{
    private readonly ListEditorDragBehavior _behavior = new();

    public ListItemEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
    }

    public sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IPropertyEditorContext viewModel)
            {
                if (viewModel.Extension.TryCreateControlForListItem(viewModel, out IListItemEditor? control))
                {
                    if (control is StyledElement s)
                        s.DataContext = viewModel;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty)
        {
            BehaviorCollection behaviors = Interaction.GetBehaviors(this);
            behaviors.Clear();
            if (change.NewValue is IListItemEditor newControl)
            {
                newControl.DeleteRequested += OnDeleteRequested;
                newControl.PropertyChanged += OnInnerPropertyChanged;
                if (newControl.ReorderHandle != null)
                {
                    _behavior.DragControl = newControl.ReorderHandle;
                    behaviors.Add(_behavior);
                }
            }

            if (change.OldValue is IListItemEditor oldControl)
            {
                oldControl.DeleteRequested -= OnDeleteRequested;
                oldControl.PropertyChanged -= OnInnerPropertyChanged;
            }
        }
    }

    private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IListItemEditor.ReorderHandle)
            && sender is IListItemEditor typedSender)
        {
            BehaviorCollection behaviors = Interaction.GetBehaviors(this);
            behaviors.Clear();
            _behavior.DragControl = typedSender.ReorderHandle!;
            behaviors.Add(_behavior);
        }
    }

    private void OnDeleteRequested(object? sender, EventArgs e)
    {
        if (DataContext is IListItemEditorViewModel viewModel)
        {
            viewModel.OnDeleteRequested();
        }
    }
}
