using System.Collections;
using System.Reflection;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using BeUtl.Commands;
using BeUtl.Controls.Behaviors;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.ViewModels.Tools;

namespace BeUtl.Views.Editors;

public sealed class ListEditorDragBehavior : GenericDragBehavior
{
    protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.Items is not IList items)
        {
            return;
        }

        items.BeginRecord()
            .Move(oldIndex, newIndex)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }
}

public partial class ListEditor : UserControl
{
    private static readonly Lazy<CrossFade> s_crossFade = new(() => new(TimeSpan.FromSeconds(0.25)));
    private CancellationTokenSource? _lastTransitionCts;

    public ListEditor()
    {
        InitializeComponent();
        toggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async value =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();

                if (value == true)
                {
                    await s_crossFade.Value.Start(null, expandItem, _lastTransitionCts.Token);
                }
                else
                {
                    await s_crossFade.Value.Start(expandItem, null, _lastTransitionCts.Token);
                }

                expandItem.IsVisible = value == true;
            });
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        progress.IsVisible = progress.IsIndeterminate = true;
        if (DataContext is ListEditorViewModel viewModel && viewModel.List.Value != null)
        {
            await Task.Run(async () =>
            {
                Type type = viewModel.WrappedProperty.Property.PropertyType;
                Type? interfaceType = Array.Find(type.GetInterfaces(), x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
                Type? itemtype = interfaceType?.GenericTypeArguments?.FirstOrDefault();
                if (itemtype != null)
                {
                    Type[] types = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(x => x.GetTypes())
                        .Where(x => !x.IsAbstract
                            && x.IsPublic
                            && x.IsAssignableTo(itemtype)
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

                            var dialog = new FA.ContentDialog
                            {
                                Content = combobox,
                                Title = S.Message.MultipleTypesAreAvailable,
                                PrimaryButtonText = S.Common.OK,
                                CloseButtonText = S.Common.Cancel
                            };

                            if (await dialog.ShowAsync() == FA.ContentDialogResult.Primary)
                            {
                                return combobox.SelectedItem as Type;
                            }
                            else
                            {
                                return null;
                            }
                        });
                    }
                    else if (itemtype.IsSealed)
                    {
                        type2 = itemtype;
                    }

                    constructorInfo = type2?.GetConstructor(Array.Empty<Type>());

                    if (constructorInfo != null)
                    {
                        object? obj = constructorInfo.Invoke(null);
                        if (obj != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => viewModel.List.Value.Add(obj));
                        }
                    }
                }
            });

            // ListがINotifyProeprtyChangedを実装していない可能性があるので
            if (viewModel.ObserveCount.Value != viewModel.List.Value.Count)
            {
                viewModel.ObserveCount.Value = viewModel.List.Value.Count;
            }
        }

        progress.IsVisible = progress.IsIndeterminate = false;
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>().DataContext is EditViewModel editViewModel
            && sender is ILogical logical
            && DataContext is ListEditorViewModel { List.Value: { } list } viewModel)
        {
            ObjectPropertyEditorViewModel objViewModel
                = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
                    ?? new ObjectPropertyEditorViewModel(editViewModel);

            Grid grid = logical.FindLogicalAncestorOfType<Grid>();
            int index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0)
            {
                switch (list[index])
                {
                    case CoreObject coreObject:
                        objViewModel.NavigateCore(coreObject, false);
                        break;
                    case Styling.Style style:
                        StyleEditorViewModel styleEditor
                            = objViewModel.ParentContext.FindToolTab<StyleEditorViewModel>()
                                ?? new StyleEditorViewModel(editViewModel);

                        styleEditor.Style.Value = style;
                        editViewModel.OpenToolTab(styleEditor);
                        break;
                }
            }
            editViewModel.OpenToolTab(objViewModel);
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem
            && DataContext is ListEditorViewModel { List.Value: { } list } viewModel)
        {
            Grid grid = menuItem.FindLogicalAncestorOfType<Grid>();
            int index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0)
            {
                list.BeginRecord()
                    .Remove(list[index])
                    .ToCommand()
                    .DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}
