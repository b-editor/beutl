using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class ChangesModel
{
    public ReactiveCollection<PackageChangeModel> InstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UninstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UpdateItems { get; } = [];

    public async Task Load(
        BeutlApiApplication apiApp,
        string[] installItems,
        string[] uninstallItems,
        string[] updateItems,
        CancellationToken cancellationToken)
    {
        var installViewModels = new List<PackageChangeModel>();
        var hash = new HashSet<string>();
        foreach (string item in installItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Install,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                installViewModels.Add(itemViewModel);
            }
        }

        var updateViewModels = new List<PackageChangeModel>();
        hash.Clear();
        foreach (string item in updateItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Update,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                updateViewModels.Add(itemViewModel);
            }
        }

        var uninstallViewModels = new List<PackageChangeModel>();
        hash.Clear();
        foreach (string item in uninstallItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                PackageChangeAction.Uninstall,
                cancellationToken);

            if (itemViewModel != null && hash.Add(itemViewModel.Id))
            {
                uninstallViewModels.Add(itemViewModel);
            }
        }

        foreach (PackageChangeModel item in installViewModels)
        {
            InstallItems.Add(item);
        }

        foreach (PackageChangeModel item in updateViewModels)
        {
            UpdateItems.Add(item);
        }

        foreach (PackageChangeModel item in uninstallViewModels)
        {
            UninstallItems.Add(item);
        }
    }
}
