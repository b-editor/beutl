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

        var saveMenu = new FAMenuFlyoutItem { Text = Strings.SaveAsTemplate };
        saveMenu.Bind(
            InputElement.IsEnabledProperty,
            dataContext.Select(d => d?.CanSaveAsTemplate ?? Observable.ReturnThenNever(false)).Switch());
        saveMenu.Click += (_, _) =>
        {
            if (control.DataContext is not BaseEditorViewModel { IsDisposed: false } vm) return;

            string defaultName = vm.GetTemplateDefaultName();
            string uniqueName = ObjectTemplateService.Instance.GetUniqueName(defaultName);

            var flyout = new SaveAsTemplateFlyout { Text = uniqueName };
            flyout.Confirmed += async (_, name) =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    await vm.SaveAsTemplateAsync(name);
                    NotificationService.ShowInformation(Strings.Templates, Strings.TemplateSaved);
                }
            };
            flyout.ShowAt(control, true);
        };

        var applySubMenu = new FAMenuFlyoutSubItem { Text = Strings.ApplyTemplate };
        var addSubMenu = new FAMenuFlyoutSubItem { Text = Strings.AddFromTemplate };

        menuFlyout.Opening += (_, _) =>
        {
            applySubMenu.Items.Clear();
            addSubMenu.Items.Clear();

            if (control.DataContext is not BaseEditorViewModel { IsDisposed: false } vm)
            {
                applySubMenu.IsVisible = true;
                addSubMenu.IsVisible = false;
                applySubMenu.IsEnabled = false;
                return;
            }

            bool isListItem = vm.IsListItemAdapter;
            bool isGroup = vm.IsTemplateGroup;
            var templates = vm.GetApplicableTemplates().ToList();

            if (isListItem)
            {
                // ListItem: 「テンプレートを適用」(置き換え) と「テンプレートから追加」(親リストに追加) の2つを表示
                applySubMenu.IsVisible = true;
                addSubMenu.IsVisible = true;

                foreach (ObjectTemplateItem t in templates)
                {
                    applySubMenu.Items.Add(CreateApplyMenuItem(t, vm));
                    addSubMenu.Items.Add(CreateAddMenuItem(t, vm, useApplyTemplate: false));
                }
            }
            else if (isGroup)
            {
                // Group: 「テンプレートから追加」のみ表示（ApplyTemplate がグループに追加する）
                applySubMenu.IsVisible = false;
                addSubMenu.IsVisible = true;

                foreach (ObjectTemplateItem t in templates)
                {
                    addSubMenu.Items.Add(CreateAddMenuItem(t, vm, useApplyTemplate: true));
                }
            }
            else
            {
                // 通常: 「テンプレートを適用」のみ
                applySubMenu.IsVisible = true;
                addSubMenu.IsVisible = false;

                foreach (ObjectTemplateItem t in templates)
                {
                    applySubMenu.Items.Add(CreateApplyMenuItem(t, vm));
                }
            }

            applySubMenu.IsEnabled = applySubMenu.Items.Count > 0;
            addSubMenu.IsEnabled = addSubMenu.Items.Count > 0;
        };

        var separator = new FAMenuFlyoutSeparator();
        separator.Bind(
            Visual.IsVisibleProperty,
            dataContext
                .Select(d => d?.CanSaveAsTemplate ?? Observable.ReturnThenNever(false))
                .Switch());
        menuFlyout.Items.Add(separator);
        menuFlyout.Items.Add(saveMenu);
        menuFlyout.Items.Add(applySubMenu);
        menuFlyout.Items.Add(addSubMenu);
    }

    private static FAMenuFlyoutItem CreateApplyMenuItem(ObjectTemplateItem template, BaseEditorViewModel vm)
    {
        var mi = new FAMenuFlyoutItem { Text = template.Name.Value, Tag = template };
        mi.Click += (s, _) =>
        {
            if (s is FAMenuFlyoutItem { Tag: ObjectTemplateItem item })
                vm.ApplyTemplate(item);
        };
        return mi;
    }

    private static FAMenuFlyoutItem CreateAddMenuItem(ObjectTemplateItem template, BaseEditorViewModel vm, bool useApplyTemplate)
    {
        var mi = new FAMenuFlyoutItem { Text = template.Name.Value, Tag = template };
        mi.Click += (s, _) =>
        {
            if (s is not FAMenuFlyoutItem { Tag: ObjectTemplateItem item }) return;
            if (useApplyTemplate)
                vm.ApplyTemplate(item);
            else
                vm.AddTemplateAsListItem(item);
        };
        return mi;
    }
}
