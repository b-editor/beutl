using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Beutl.Commands;
using Beutl.Services.Editors.Wrappers;
using Beutl.Styling;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

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

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        OnDelete();
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

            objViewModel.NavigateCore(viewModel.Value.Value, false);
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
                Type type = viewModel.WrappedProperty.Property.PropertyType;
                Type[] types = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .Where(x => !x.IsAbstract
                        && x.IsPublic
                        && x.IsAssignableTo(type)
                        && x.GetConstructor(Array.Empty<Type>()) != null)
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
                            Items = types,
                            SelectedIndex = 0
                        };

                        var dialog = new ContentDialog
                        {
                            Content = combobox,
                            Title = S.Message.MultipleTypesAreAvailable,
                            PrimaryButtonText = S.Common.OK,
                            CloseButtonText = S.Common.Cancel
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

                constructorInfo = type2?.GetConstructor(Array.Empty<Type>());

                if (constructorInfo?.Invoke(null) is T typed)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => viewModel.SetValue(viewModel.Value.Value, typed));
                }
            });
        }

        //progress.IsVisible = false;
    }

    protected override void OnDelete()
    {
        if (DataContext is NavigationButtonViewModel<T> viewModel)
        {
            if (this.FindLogicalAncestorOfType<StyleEditor>()?.DataContext is StyleEditorViewModel parentViewModel
                && viewModel.WrappedProperty is IStylingSetterWrapper wrapper
                && parentViewModel.Style.Value is Style style)
            {
                style.Setters.BeginRecord<ISetter>()
                    .Remove(wrapper.Setter)
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
            else if (viewModel.Value.Value is T obj)
            {
                viewModel.SetValue(obj, default);
            }
        }
    }
}
