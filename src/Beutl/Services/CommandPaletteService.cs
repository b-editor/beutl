using System.Runtime.InteropServices;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

namespace Beutl.Services;

public sealed class CommandPaletteService
{
    private static readonly OSPlatform s_currentPlatform =
        OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsLinux() ? OSPlatform.Linux :
        OSPlatform.OSX;

    private readonly ContextCommandManager? _commandManager;
    private readonly ICommandPaletteHandlerProvider _handlerProvider;
    private readonly Func<MenuBarViewModel?> _menuBarAccessor;
    private readonly Dictionary<Type, string> _categoryNameCache = new();

    public CommandPaletteService(
        ContextCommandManager? commandManager,
        ICommandPaletteHandlerProvider handlerProvider,
        Func<MenuBarViewModel?> menuBarAccessor)
    {
        _commandManager = commandManager;
        _handlerProvider = handlerProvider;
        _menuBarAccessor = menuBarAccessor;
    }

    public IReadOnlyList<PaletteCommand> EnumerateCommands()
    {
        var result = new List<PaletteCommand>();

        if (_commandManager != null)
        {
            foreach (ContextCommandEntry entry in _commandManager.GetDefinitions())
            {
                if (entry.Definition.Name == MainViewExtension.ShowCommandPaletteCommandName)
                {
                    continue;
                }

                string displayName = string.IsNullOrEmpty(entry.Definition.DisplayName)
                    ? entry.Definition.Name
                    : entry.Definition.DisplayName!;
                string category = ResolveCategoryName(entry.ExtensionType);
                KeyGesture? gesture = entry.KeyGestures
                    .FirstOrDefault(i => i.Platform == s_currentPlatform)?.KeyGesture;

                ContextCommandEntry capturedEntry = entry;
                result.Add(new PaletteCommand(
                    Id: $"{entry.ExtensionType.FullName}.{entry.Definition.Name}",
                    DisplayName: displayName,
                    Description: entry.Definition.Description,
                    CategoryName: category,
                    KeyGesture: gesture,
                    CanExecute: () => CanExecuteContextCommand(capturedEntry),
                    Execute: () =>
                    {
                        var handler = _handlerProvider.Resolve(capturedEntry.ExtensionType);
                        handler?.Execute(new ContextCommandExecution(capturedEntry.Definition.Name));
                    }));
            }
        }

        AppendMenuCommands(result);

        return result;
    }

    private bool CanExecuteContextCommand(ContextCommandEntry entry)
    {
        IContextCommandHandler? handler = _handlerProvider.Resolve(entry.ExtensionType);
        if (handler == null)
        {
            return false;
        }

        if (entry.ExtensionType == typeof(MainViewExtension))
        {
            MenuBarViewModel? menuBar = _menuBarAccessor();
            if (menuBar != null && TryGetMenuBarCommand(menuBar, entry.Definition.Name) is { } command)
            {
                return command.CanExecute(null);
            }
        }

        return true;
    }

    private static System.Windows.Input.ICommand? TryGetMenuBarCommand(MenuBarViewModel menuBar, string name) => name switch
    {
        "CreateNewProject" => menuBar.CreateNewProject,
        "CreateNewFile" => menuBar.CreateNew,
        "OpenProject" => menuBar.OpenProject,
        "OpenFile" => menuBar.OpenFile,
        "Save" => menuBar.Save,
        "SaveAll" => menuBar.SaveAll,
        "CloseProject" => menuBar.CloseProject,
        "Undo" => menuBar.Undo,
        "Redo" => menuBar.Redo,
        "Exit" => menuBar.Exit,
        _ => null
    };

    private string ResolveCategoryName(Type extensionType)
    {
        if (_categoryNameCache.TryGetValue(extensionType, out string? cached))
        {
            return cached;
        }

        string name = extensionType.Name;
        Extension? matched = ExtensionProvider.Current.AllExtensions
            .FirstOrDefault(i => i.GetType() == extensionType);
        if (matched != null && !string.IsNullOrEmpty(matched.DisplayName))
        {
            name = matched.DisplayName;
        }

        _categoryNameCache[extensionType] = name;
        return name;
    }

    private void AppendMenuCommands(List<PaletteCommand> commands)
    {
        MenuBarViewModel? menuBar = _menuBarAccessor();
        if (menuBar == null)
        {
            return;
        }

        string category = Strings.CommandPalette_MenuCategory;

        foreach (MenuBarViewModel.PaletteMenuCommand menuCommand in menuBar.EnumeratePaletteCommands())
        {
            System.Windows.Input.ICommand command = menuCommand.Command;
            commands.Add(new PaletteCommand(
                Id: menuCommand.Id,
                DisplayName: menuCommand.DisplayName,
                Description: null,
                CategoryName: category,
                KeyGesture: null,
                CanExecute: () => command.CanExecute(null),
                Execute: () =>
                {
                    if (command.CanExecute(null))
                        command.Execute(null);
                }));
        }
    }
}
