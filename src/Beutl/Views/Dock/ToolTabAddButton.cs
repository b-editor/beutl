using Avalonia.Controls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dock;

public sealed class ToolTabAddButton : Button
{
    protected override void OnClick()
    {
        base.OnClick();

        ContextFlyout?.Hide();
        ContextFlyout = CreateContextFlyout();
        ContextFlyout?.ShowAt(this);
    }

    internal FAMenuFlyout? CreateContextFlyout()
    {
        if (DataContext is not IToolDock target
            || target.Factory is not BeutlDockFactory factory)
        {
            return null;
        }

        FAMenuFlyoutItem[] CreateItems()
        {
            return factory.EnumerateToolTabExtensions()
                .Select(extension => CreateMenuItem(factory, target, extension))
                .ToArray();
        }

        var menu = new FAMenuFlyout();
        foreach (var item in CreateItems()) menu.Items.Add(item);
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in CreateItems()) menu.Items.Add(item);
        };
        return menu;
    }

    private static FAMenuFlyoutItem CreateMenuItem(
        BeutlDockFactory factory,
        IToolDock target,
        ToolTabExtension extension)
    {
        var item = new FAMenuFlyoutItem
        {
            DataContext = extension,
            Text = extension.Header,
            IsEnabled = extension.CanMultiple || !factory.IsToolTabOpen(extension),
        };

        item.Click += (_, _) => factory.OpenToolTab(extension, target);
        return item;
    }
}
