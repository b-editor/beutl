using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class NavigateButton : UserControl
{
    public NavigateButton()
    {
        InitializeComponent();
    }

    protected virtual void OnNavigate()
    {
    }

    protected virtual void OnNew()
    {
    }

    protected virtual void OnDelete()
    {
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        OnNavigate();
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void New_Click(object? sender, RoutedEventArgs e)
    {
        OnNew();
    }
}

public sealed class NavigateButton<T> : NavigateButton
    where T : ICoreObject
{
    protected override void OnNavigate()
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is NavigationButtonViewModel<T> viewModel)
        {
            ObjectPropertyEditorViewModel objViewModel
                = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
                    ?? new ObjectPropertyEditorViewModel(editViewModel);

            objViewModel.NavigateCore(viewModel.Value.Value, false, viewModel);
            editViewModel.OpenToolTab(objViewModel);
        }
    }

    protected override async void OnNew()
    {
        //progress.IsVisible = true;
        if (DataContext is NavigationButtonViewModel<T> viewModel)
        {
            await Task.Run(async () =>
            {
                Type type = viewModel.WrappedProperty.PropertyType;
                Type[] types = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => !x.IsAbstract
                        && x.IsPublic
                        && x.IsAssignableTo(type)
                        && x.GetConstructor([]) != null)
                    .ToArray();
                Type? type2 = null;
                ConstructorInfo? constructorInfo = null;

                if (types.Length == 1)
                {
                    type2 = types[0];
                }
                else if (types.Length > 1)
                {
                    type2 = await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var combobox = new ComboBox
                        {
                            ItemsSource = types,
                            SelectedIndex = 0
                        };

                        var dialog = new ContentDialog
                        {
                            Content = combobox,
                            Title = Message.MultipleTypesAreAvailable,
                            PrimaryButtonText = Strings.OK,
                            CloseButtonText = Strings.Cancel
                        };

                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            return combobox.SelectedItem as Type;
                        }
                        else
                        {
                            return null;
                        }
                    });
                }
                else if (type.IsSealed)
                {
                    type2 = type;
                }

                constructorInfo = type2?.GetConstructor([]);

                if (constructorInfo?.Invoke(null) is T typed)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => viewModel.SetValue(viewModel.Value.Value, typed));
                }
            });
        }

        //progress.IsVisible = false;
    }
}
