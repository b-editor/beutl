using Beutl.PackageTools.UI.Models;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.ViewModels;

public class ActionViewModel
{
    public ActionViewModel(ChangesModel changesModel, PackageChangeModel model)
    {
        Model = model;
        if (model.Action == PackageChangeAction.Install)
        {
            Title = $"{Strings.Install} ({changesModel.InstallItems.IndexOf(model) + 1}/{changesModel.InstallItems.Count})";
        }
        else if (model.Action == PackageChangeAction.Uninstall)
        {
            Title = $"{Strings.Uninstall} ({changesModel.UninstallItems.IndexOf(model) + 1}/{changesModel.UninstallItems.Count})";
        }
        else if (model.Action == PackageChangeAction.Update)
        {
            Title = $"{Strings.Update} ({changesModel.UpdateItems.IndexOf(model) + 1}/{changesModel.UpdateItems.Count})";
        }
        else
        {
            Title = "";
        }
    }

    public string Title { get; }

    public string? LogoUrl => Model.LogoUrl;

    public string DisplayName => Model.DisplayName;

    public string Version => Model.Version.ToString();

    public string Publisher => Model.Publisher ?? "不明な発行者";

    public string? Description => Model.Description;

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public PackageChangeModel Model { get; }
}
