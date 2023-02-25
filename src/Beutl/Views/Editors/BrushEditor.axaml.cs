using System.Reflection;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Media;
using Beutl.Operation;
using Beutl.Styling;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public sealed partial class BrushEditor : UserControl
{
    public BrushEditor()
    {
        InitializeComponent();
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is BrushEditorViewModel viewModel
            && viewModel.Value.Value is ICoreObject obj)
        {
            ObjectPropertyEditorViewModel objViewModel
                = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
                    ?? new ObjectPropertyEditorViewModel(editViewModel);

            objViewModel.NavigateCore(obj, false);
            editViewModel.OpenToolTab(objViewModel);
        }
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private async void New_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
            (Type, string)[] items = new (Type, string)[]
            {
                (typeof(SolidColorBrush), "SolidColorBrush"),
                (typeof(ImageBrush), "ImageBrush"),
                (typeof(ConicGradientBrush), "ConicGradientBrush"),
                (typeof(LinearGradientBrush), "LinearGradientBrush"),
                (typeof(RadialGradientBrush), "RadialGradientBrush"),
            };

            var combobox = new ComboBox
            {
                Items = items,
                SelectedIndex = 0,
                ItemTemplate = new FuncDataTemplate<(Type, string)>(
                    match: _ => true,
                    build: (x, _) => new TextBlock { Text = x.Item2 })
            };

            var dialog = new ContentDialog
            {
                Content = combobox,
                Title = Message.MultipleTypesAreAvailable,
                PrimaryButtonText = Strings.OK,
                CloseButtonText = Strings.Cancel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && combobox.SelectedItem is (Type type, string _))
            {
                ConstructorInfo? constructorInfo = type.GetConstructor(Array.Empty<Type>());

                if (constructorInfo?.Invoke(null) is IBrush typed)
                {
                    viewModel.SetValue(viewModel.Value.Value, typed);
                }
            }
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
            if (this.FindLogicalAncestorOfType<StyleEditor>()?.DataContext is StyleEditorViewModel parentViewModel
                && viewModel.WrappedProperty is IStylingSetterPropertyImpl wrapper
                && parentViewModel.Style.Value is Style style)
            {
                style.Setters.BeginRecord<ISetter>()
                    .Remove(wrapper.Setter)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
            else
            {
                viewModel.SetValue(viewModel.Value.Value, default);
            }
        }
    }
}
