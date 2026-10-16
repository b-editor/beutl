using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    // MainView runs these, since it owns the dialogs and windows they open.
    public ReactiveCommandSlim ShowSettings { get; } = new();

    public ReactiveCommandSlim<ToolWindowExtension> OpenToolWindow { get; } = new();

    // MainViewModel runs these.
    public ReactiveCommandSlim<ToolTabExtension> OpenToolTab { get; } = new();

    public ReactiveCommandSlim<EditorExtension> SwitchEditor { get; } = new();
}
