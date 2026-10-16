using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.Services;

public interface ICommandPaletteHandlerProvider
{
    // The open context that runs the extension's commands: the main view model, an editor or a tool context.
    object? Resolve(Type extensionType);
}

internal sealed class CommandPaletteHandlerProvider : ICommandPaletteHandlerProvider
{
    private readonly Func<MainViewModel?> _mainViewModelAccessor;
    private readonly EditorService _editorService;

    public CommandPaletteHandlerProvider(Func<MainViewModel?> mainViewModelAccessor, EditorService editorService)
    {
        _mainViewModelAccessor = mainViewModelAccessor;
        _editorService = editorService;
    }

    public object? Resolve(Type extensionType)
    {
        if (extensionType == typeof(MainViewExtension))
        {
            return _mainViewModelAccessor();
        }

        if (typeof(EditorExtension).IsAssignableFrom(extensionType))
        {
            EditorTabItem? tab = _editorService.SelectedTabItem.Value;
            if (tab?.Extension.Value is { } extension && extension.GetType() == extensionType)
            {
                return tab.Context.Value;
            }

            return null;
        }

        if (typeof(ToolTabExtension).IsAssignableFrom(extensionType))
        {
            EditorTabItem? tab = _editorService.SelectedTabItem.Value;
            if (tab?.Context.Value is EditViewModel editor)
            {
                return editor.DockHost.FindToolContext(extensionType);
            }

            return null;
        }

        return null;
    }
}
