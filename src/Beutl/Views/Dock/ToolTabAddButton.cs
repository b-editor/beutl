using Avalonia.Controls;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;

namespace Beutl.Views.Dock;

public sealed class ToolTabAddButton : Button
{
    protected override void OnClick()
    {
        base.OnClick();

        ContextMenu?.Close();
        ContextMenu = CreateContextMenu();
        ContextMenu?.Open();
    }

    internal ContextMenu? CreateContextMenu()
    {
        if (DataContext is not IToolDock target
            || target.Factory is not BeutlDockFactory factory)
        {
            return null;
        }

        var items = factory.EnumerateToolTabExtensions()
            .Select(extension => CreateMenuItem(factory, target, extension))
            .ToArray();

        return new ContextMenu
        {
            ItemsSource = items,
        };
    }

    private static MenuItem CreateMenuItem(
        BeutlDockFactory factory,
        IToolDock target,
        ToolTabExtension extension)
    {
        var item = new MenuItem
        {
            DataContext = extension,
            Header = extension.Header,
            IsEnabled = extension.CanMultiple || !factory.IsToolTabOpen(extension),
        };

        item.Click += (_, _) => factory.OpenToolTab(extension, target);
        return item;
    }
}
