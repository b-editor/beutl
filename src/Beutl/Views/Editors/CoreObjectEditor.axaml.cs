using System;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class CoreObjectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;

    public CoreObjectEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
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

    private void NewClick(object? sender, RoutedEventArgs e)
    {
        OnNew();
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        OnDelete();
    }
}

public sealed class CoreObjectEditor<T> : CoreObjectEditor
    where T : ICoreObject
{
    protected override void OnNavigate()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> viewModel) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyEditorViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
              ?? new ObjectPropertyEditorViewModel(editViewModel);

        objViewModel.NavigateCore(viewModel.Value.Value, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    protected override async void OnNew()
    {
        //progress.IsVisible = true;
        if (DataContext is CoreObjectEditorViewModel<T> viewModel)
        {
            await Task.Run(async () =>
            {
                Type type = viewModel.PropertyAdapter.PropertyType;
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
                        var combobox = new ComboBox { ItemsSource = types, SelectedIndex = 0 };

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
