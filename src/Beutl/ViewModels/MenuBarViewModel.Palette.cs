using System.Windows.Input;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    public readonly record struct PaletteMenuCommand(string Id, string DisplayName, ICommand Command);

    public IEnumerable<PaletteMenuCommand> EnumeratePaletteCommands()
    {
        yield return new("MenuBar.ExportProject", Strings.ExportProject, ExportProject);
        yield return new("MenuBar.ImportProject", Strings.ImportProject, ImportProject);
        yield return new("MenuBar.DeleteLayer", Strings.Delete, DeleteLayer);
        yield return new("MenuBar.ExcludeLayer", Strings.Exclude, ExcludeLayer);
        yield return new("MenuBar.CutLayer", Strings.Cut, CutLayer);
        yield return new("MenuBar.CopyLayer", Strings.Copy, CopyLayer);
        yield return new("MenuBar.PasteLayer", Strings.Paste, PasteLayer);
        yield return new("MenuBar.ResetDockLayout", Strings.ResetDockLayout, ResetDockLayout);
        yield return new("MenuBar.ShowSceneSettings", Strings.SceneSettings, ShowSceneSettings);
    }

    internal static Task ExecuteCommandAsync(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return Task.CompletedTask;
        }

        if (command is AsyncReactiveCommand asyncCommand)
        {
            return asyncCommand.ExecuteAsync(null!);
        }

        command.Execute(null);
        return Task.CompletedTask;
    }
}
