using System.Reactive.Linq;

using Beutl.PackageTools.UI.Models;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class ActionViewModel
{
    public ActionViewModel(ChangesModel changesModel, PackageChangeModel model)
    {
        Model = model;
        Title = model.Action switch
        {
            PackageChangeAction.Install => $"{Strings.Install} ({changesModel.InstallItems.IndexOf(model) + 1}/{changesModel.InstallItems.Count})",
            PackageChangeAction.Uninstall => $"{Strings.Uninstall} ({changesModel.UninstallItems.IndexOf(model) + 1}/{changesModel.UninstallItems.Count})",
            PackageChangeAction.Update => $"{Strings.Update} ({changesModel.UpdateItems.IndexOf(model) + 1}/{changesModel.UpdateItems.Count})",
            _ => throw new ArgumentOutOfRangeException(nameof(model), model.Action, null),
        };

        Finished = Succeeded.CombineLatest(Failed, Canceled)
            .Select(t => t.First || t.Second || t.Third)
            .ToReadOnlyReactiveProperty();
    }

    public string Title { get; }

    public string? LogoUrl => Model.LogoUrl;

    public string DisplayName => Model.DisplayName;

    public string Version => Model.Version.ToString();

    public string Publisher => Model.Publisher ?? Strings.Unknown;

    public string? Description => Model.Description;

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public ReactiveProperty<bool> Canceled { get; } = new();

    public ReadOnlyReactiveProperty<bool> Finished { get; }

    public PackageChangeModel Model { get; }
}
