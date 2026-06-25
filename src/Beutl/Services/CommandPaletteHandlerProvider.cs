using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.Services;

public interface ICommandPaletteHandlerProvider
{
    IContextCommandHandler? Resolve(Type extensionType);
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

    public IContextCommandHandler? Resolve(Type extensionType)
    {
        if (extensionType == typeof(MainViewExtension))
        {
            return _mainViewModelAccessor() as IContextCommandHandler;
        }

        if (typeof(EditorExtension).IsAssignableFrom(extensionType))
        {
            EditorTabItem? tab = _editorService.SelectedTabItem.Value;
            if (tab?.Extension.Value is { } extension && extension.GetType() == extensionType)
            {
                return tab.Context.Value as IContextCommandHandler;
            }

            return null;
        }

        if (typeof(ToolTabExtension).IsAssignableFrom(extensionType))
        {
            EditorTabItem? tab = _editorService.SelectedTabItem.Value;
            if (tab?.Context.Value is EditViewModel editor)
            {
                return editor.DockHost.FindToolContext(extensionType) as IContextCommandHandler;
            }

            return null;
        }

        return null;
    }
}
