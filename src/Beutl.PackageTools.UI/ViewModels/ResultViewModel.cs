using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class ResultViewModel
{
    public ResultViewModel(
        ActionViewModel[] install,
        ActionViewModel[] uninstall,
        ActionViewModel[] update,
        CleanViewModel? clean)
    {
        Install = install;
        Uninstall = uninstall;
        Update = update;
        Clean = clean;
    }

    public ActionViewModel[] Install { get; }

    public ActionViewModel[] Uninstall { get; }

    public ActionViewModel[] Update { get; }

    public CleanViewModel? Clean { get; }

    public ReactiveProperty<ActionViewModel> SelectedItem { get; } = new();
}
