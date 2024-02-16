using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class ResultViewModel
{
    public ResultViewModel(
        ActionViewModel[] install,
        ActionViewModel[] uninstall,
        ActionViewModel[] update)
    {
        Install = install;
        Uninstall = uninstall;
        Update = update;
    }

    public ActionViewModel[] Install { get; }

    public ActionViewModel[] Uninstall { get; }

    public ActionViewModel[] Update { get; }

    public ReactiveProperty<ActionViewModel> SelectedItem { get; } = new();
}
