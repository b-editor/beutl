using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public static class TemplateMenuHelper
{
    public static void AddMenus(FAMenuFlyout menuFlyout, Control control)
    {
        var dataContext = control.GetObservable(StyledElement.DataContextProperty)
            .Select(obj => obj as BaseEditorViewModel);

        var saveMenu = new MenuFlyoutItem { Text = Strings.SaveAsTemplate };
        saveMenu.Bind(
            InputElement.IsEnabledProperty,
            dataContext.Select(d => d?.CanSaveAsTemplate ?? Observable.ReturnThenNever(false)).Switch());
        saveMenu.Click += (_, _) =>
        {
            if (control.DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;
            var flyout = new SaveAsTemplateFlyout();
            flyout.Confirmed += async (_, name) =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await vm.SaveAsTemplateAsync(name);
                    NotificationService.ShowInformation(Strings.Templates, Strings.TemplateSaved);
                }
            };
            flyout.ShowAt(control);
        };

        var applySubMenu = new MenuFlyoutSubItem { Text = Strings.ApplyTemplate };

        menuFlyout.Opening += (_, _) =>
        {
            applySubMenu.Items.Clear();
            if (control.DataContext is BaseEditorViewModel { IsDisposed: false } vm)
            {
                foreach (ObjectTemplateItem t in vm.GetApplicableTemplates())
                {
                    var mi = new MenuFlyoutItem { Text = t.Name.Value, Tag = t };
                    mi.Click += (s, _) =>
                    {
                        if (s is MenuFlyoutItem { Tag: ObjectTemplateItem item })
                            vm.ApplyTemplate(item);
                    };
                    applySubMenu.Items.Add(mi);
                }
            }

            applySubMenu.IsEnabled = applySubMenu.Items.Count > 0;
        };

        var separator = new MenuFlyoutSeparator();
        separator.Bind(
            Visual.IsVisibleProperty,
            dataContext
                .Select(d => d?.CanSaveAsTemplate ?? Observable.ReturnThenNever(false))
                .Switch());
        menuFlyout.Items.Add(separator);
        menuFlyout.Items.Add(saveMenu);
        menuFlyout.Items.Add(applySubMenu);
    }
}
