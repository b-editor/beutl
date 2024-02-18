using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class ChangesModel
{
    public ReactiveCollection<PackageChangeModel> InstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UninstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UpdateItems { get; } = [];

    public async Task Load(
        BeutlApiApplication apiApp,
        string[] installItems, string[] uninstallItems, string[] updateItems)
    {
        var hash = new HashSet<string>();
        foreach (string item in installItems)
        {
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(apiApp, item, PackageChangeAction.Install);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                InstallItems.Add(itemViewModel);
            }
        }

        hash.Clear();
        foreach (string item in updateItems)
        {
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(apiApp, item, PackageChangeAction.Uninstall);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                UpdateItems.Add(itemViewModel);
            }
        }

        hash.Clear();
        foreach (string item in uninstallItems)
        {
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(apiApp, item, PackageChangeAction.Uninstall);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                UninstallItems.Add(itemViewModel);
            }
        }
    }
}
