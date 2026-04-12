using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public static class CopyPasteMenuHelper
{
    public static void AddMenus(FAMenuFlyout menuFlyout, Control control)
    {
        var dataContext = control.GetObservable(StyledElement.DataContextProperty)
            .Select(obj => obj as BaseEditorViewModel);
        var copyMenu = new MenuFlyoutItem { Text = Strings.Copy };
        copyMenu.Bind(
            InputElement.IsEnabledProperty,
            dataContext.Select(d => d?.CanCopy ?? Observable.ReturnThenNever(false)).Switch());
        copyMenu.Click += async (_, _) =>
        {
            if (control.DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
            try
            {
                await vm.CopyAsync();
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        };
        var pasteMenu = new MenuFlyoutItem { Text = Strings.Paste };
        pasteMenu.Bind(
            InputElement.IsEnabledProperty,
            dataContext.Select(d => d?.CanPaste ?? Observable.ReturnThenNever(false)).Switch());
        pasteMenu.Click += async (_, _) =>
        {
            if (control.DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
            try
            {
                if (!await vm.PasteAsync())
                {
                    NotificationService.ShowInformation(Strings.Paste, MessageStrings.CannotPasteFromClipboard);
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        };

        menuFlyout.Opening += async (sender, args) =>
        {
            if (control.DataContext is BaseEditorViewModel { IsDisposed: false } vm)
            {
                await vm.RefreshCanPasteAsync();
            }
        };

        if (menuFlyout.Items.Count > 0)
        {
            var separator = new MenuFlyoutSeparator();
            separator.Bind(
                Visual.IsVisibleProperty,
                dataContext
                    .Select(d => d != null
                        ? d.CanCopy.CombineLatest(d.CanPaste, (canCopy, canPaste) => canCopy || canPaste)
                        : Observable.ReturnThenNever(false))
                    .Switch());
            menuFlyout.Items.Add(separator);
        }

        menuFlyout.Items.Add(copyMenu);
        menuFlyout.Items.Add(pasteMenu);
    }
}
