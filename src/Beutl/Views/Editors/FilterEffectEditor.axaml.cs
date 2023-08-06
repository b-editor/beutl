using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Graphics.Effects;
using Beutl.Services;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class FilterEffectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    public FilterEffectEditor()
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

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(KnownLibraryItemFormats.FilterEffect) is Type type
            && DataContext is FilterEffectEditorViewModel viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(type);
            }
            else
            {
                viewModel.ChangeFilterType(type);
            }

            viewModel.IsExpanded.Value = true;
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.FilterEffect))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilterEffectEditorViewModel viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                Type? type = await SelectType();
                if (type != null)
                {
                    try
                    {
                        viewModel.AddItem(type);
                    }
                    catch (Exception ex)
                    {
                        ServiceLocator.Current.GetRequiredService<INotificationService>()
                            .Show(new("Error", ex.Message, NotificationType.Error));
                    }
                }
            }
            else
            {
                expandToggle.ContextFlyout?.ShowAt(expandToggle);
            }
        }
    }

    private static async Task<Type?> SelectType()
    {
        Type[] availableTypes = await Task.Run(() =>
        {
            Type itemType = typeof(FilterEffect);
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => !x.IsAbstract
                    && x.IsPublic
                    && x.IsAssignableTo(itemType)
                    && (itemType.GetConstructor(Array.Empty<Type>()) != null
                    || itemType.GetConstructors().Length == 0))
                .ToArray();
        });

        var combobox = new ComboBox
        {
            ItemsSource = availableTypes,
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
    }

    private async void ChangeFilterTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilterEffectEditorViewModel viewModel)
        {
            Type? type = await SelectType();
            if (type != null)
            {
                try
                {
                    viewModel.ChangeFilterType(type);
                }
                catch (Exception ex)
                {
                    ServiceLocator.Current.GetRequiredService<INotificationService>()
                        .Show(new("Error", ex.Message, NotificationType.Error));
                }
            }
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FilterEffectEditorViewModel viewModel)
        {
            viewModel.SetNull();
        }
    }
}
