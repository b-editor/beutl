using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class FilterEffectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;
    private UnknownObjectView? _unknownObjectView;

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

        this.GetObservable(DataContextProperty)
            .Select(x => x as FilterEffectEditorViewModel)
            .Select(x => x?.IsDummy.Select(_ => x) ?? Observable.Return<FilterEffectEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsDummy.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _unknownObjectView = new UnknownObjectView();
                content.Children.Add(_unknownObjectView);
            });
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
            e.Handled = true;
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
                        NotificationService.ShowError("Error", ex.Message);
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
        var viewModel = new SelectFilterEffectTypeViewModel();
        var dialog = new SelectFilterEffectType
        {
            DataContext = viewModel
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (viewModel.SelectedItem.Value is SingleTypeLibraryItem single)
            {
                return single.ImplementationType;
            }
            else if (viewModel.SelectedItem.Value is MultipleTypeLibraryItem multi)
            {
                return multi.Types.GetValueOrDefault(KnownLibraryItemFormats.FilterEffect);
            }
        }

        return null;
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
                    NotificationService.ShowError("Error", ex.Message);
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
