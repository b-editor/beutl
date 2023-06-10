using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Controls.Behaviors;
using Beutl.Framework.Service;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed class ListEditorDragBehavior : GenericDragBehavior
{
    protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.DataContext is IListEditorViewModel viewModel)
        {
            viewModel.MoveItem(oldIndex, newIndex);
        }
    }
}

public partial class ListEditor : UserControl
{
    private static readonly Lazy<CrossFade> s_crossFade = new(() => new(TimeSpan.FromSeconds(0.25)));
    private CancellationTokenSource? _lastTransitionCts;

    public ListEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
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

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        OnInitializeClick();
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        OnDeleteClick();
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        await OnAddClick(sender);
    }

    protected virtual void OnInitializeClick()
    {
    }

    protected virtual void OnDeleteClick()
    {
    }

    protected virtual ValueTask OnAddClick(object? sender)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class ListEditor<TItem> : ListEditor
{
    protected override void OnInitializeClick()
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            try
            {
                viewModel.Initialize();
            }
            catch (InvalidOperationException ex)
            {
                ServiceLocator.Current.GetRequiredService<INotificationService>()
                    .Show(new("Error", ex.Message, NotificationType.Error));
            }
        }
    }

    protected override void OnDeleteClick()
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            try
            {
                viewModel.Delete();
            }
            catch (InvalidOperationException ex)
            {
                ServiceLocator.Current.GetRequiredService<INotificationService>()
                    .Show(new("Error", ex.Message, NotificationType.Error));
            }
        }
    }

    protected override async ValueTask OnAddClick(object? sender)
    {
        if (DataContext is ListEditorViewModel<TItem> viewModel)
        {
            if (viewModel.List.Value == null && sender is Button btn)
            {
                btn.ContextFlyout?.ShowAt(btn);
            }
            else if (viewModel.List.Value != null)
            {
                progress.IsVisible = progress.IsIndeterminate = true;

                await Task.Run(async () =>
                {
                    Type itemType = typeof(TItem);
                    Type[]? availableTypes = null;

                    if (itemType.IsSealed
                        && (itemType.GetConstructor(Array.Empty<Type>()) != null
                        || itemType.GetConstructors().Length == 0))
                    {
                        availableTypes = new[] { itemType };
                    }
                    else
                    {
                        availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(x => x.GetTypes())
                            .Where(x => !x.IsAbstract
                                && x.IsPublic
                                && x.IsAssignableTo(itemType)
                                && (itemType.GetConstructor(Array.Empty<Type>()) != null
                                || itemType.GetConstructors().Length == 0))
                            .ToArray();
                    }

                    Type? selectedType = null;

                    if (availableTypes.Length == 1)
                    {
                        selectedType = availableTypes[0];
                    }
                    else if (availableTypes.Length > 1)
                    {
                        selectedType = await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var combobox = new ComboBox
                            {
                                Items = availableTypes,
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

                    if (selectedType != null && Activator.CreateInstance(selectedType) is TItem item)
                    {
                        viewModel.AddItem(item);
                    }
                    else
                    {
                        ServiceLocator.Current.GetRequiredService<INotificationService>()
                            .Show(new("Error", "ListEditor<TItem>.OnAddClick", NotificationType.Error));
                    }
                });

                progress.IsVisible = progress.IsIndeterminate = false;
            }
        }
    }
}
