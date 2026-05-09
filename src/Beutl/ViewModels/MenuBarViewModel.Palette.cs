using System.Windows.Input;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    public readonly record struct PaletteMenuCommand(string Id, string DisplayName, ICommand Command);

    public IEnumerable<PaletteMenuCommand> EnumeratePaletteCommands()
    {
        yield return new("MenuBar.NewScene", Strings.CreateNewScene, NewScene);
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

    // MainViewExtension の ContextCommand 名から MenuBar 上の ICommand への単一マッピング。
    // MainViewModel.Execute / CanExecute と CommandPalette が共通で参照するためここに集約している。
    public ICommand? FindContextCommand(string commandName) => commandName switch
    {
        "CreateNewProject" => CreateNewProject,
        "CreateNewFile" => CreateNew,
        "OpenProject" => OpenProject,
        "OpenFile" => OpenFile,
        "Save" => Save,
        "SaveAll" => SaveAll,
        "CloseProject" => CloseProject,
        "Undo" => Undo,
        "Redo" => Redo,
        "Exit" => Exit,
        _ => null
    };
}
