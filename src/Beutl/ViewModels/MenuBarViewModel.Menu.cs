using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    // MainView runs these, since it owns the dialogs and windows they open.
    public ReactiveCommandSlim ShowSettings { get; } = new();

    public ReactiveCommandSlim<ToolWindowExtension> OpenToolWindow { get; } = new();

    public ReactiveCommandSlim ShowTutorials { get; } = new();

    public ReactiveCommandSlim ShowInformation { get; } = new();

    public ReactiveCommandSlim CollectGarbage { get; } = new();

    public ReactiveCommandSlim MonitorKeyModifiers { get; } = new();

    public ReactiveCommandSlim ThrowUnhandledException { get; } = new();

    public ReactiveCommandSlim StartWindowCapture { get; } = new();

    public ReactiveCommandSlim StopWindowCapture { get; } = new();

    // MainViewModel runs these.
    public ReactiveCommandSlim<ToolTabExtension> OpenToolTab { get; } = new();

    public ReactiveCommandSlim<EditorExtension> SwitchEditor { get; } = new();
}
