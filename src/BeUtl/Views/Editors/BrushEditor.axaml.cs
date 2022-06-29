using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Templates;

using BeUtl.Commands;
using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Styling;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

using ContentDialog = FluentAvalonia.UI.Controls.ContentDialog;
using ContentDialogResult = FluentAvalonia.UI.Controls.ContentDialogResult;

namespace BeUtl.Views.Editors;

public partial class BrushEditor : UserControl
{
    public BrushEditor()
    {
        InitializeComponent();
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>().DataContext is EditViewModel editViewModel
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
                Title = "複数の型が利用可能です",
                PrimaryButtonText = "決定",
                CloseButtonText = "キャンセル"
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && combobox.SelectedItem is (Type type, string _))
            {
                var constructorInfo = type.GetConstructor(Array.Empty<Type>());

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
                && viewModel.WrappedProperty is IStylingSetterWrapper wrapper
                && parentViewModel.Style.Value is Style style
                && wrapper.Tag is ISetter setter)
            {
                new RemoveCommand<ISetter>(style.Setters, setter).DoAndRecord(CommandRecorder.Default);
            }
            else
            {
                viewModel.SetValue(viewModel.Value.Value, default);
            }
        }
    }
}
