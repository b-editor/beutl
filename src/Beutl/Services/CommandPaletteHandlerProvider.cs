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

    public CommandPaletteHandlerProvider(Func<MainViewModel?> mainViewModelAccessor)
    {
        _mainViewModelAccessor = mainViewModelAccessor;
    }

    public IContextCommandHandler? Resolve(Type extensionType)
    {
        if (extensionType == typeof(MainViewExtension))
        {
            return _mainViewModelAccessor() as IContextCommandHandler;
        }

        if (typeof(EditorExtension).IsAssignableFrom(extensionType))
        {
            return EditorService.Current.SelectedTabItem.Value?.Context.Value as IContextCommandHandler;
        }

        return null;
    }
}
