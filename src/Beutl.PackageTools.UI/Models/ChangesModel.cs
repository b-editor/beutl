using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class ChangesModel
{
    public ReactiveCollection<PackageChangeModel> InstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UninstallItems { get; } = [];

    public ReactiveCollection<PackageChangeModel> UpdateItems { get; } = [];

    public Task Load(
        BeutlApiApplication apiApp,
        string[] installItems,
        string[] uninstallItems,
        string[] updateItems,
        CancellationToken cancellationToken)
        => Load(apiApp, installItems, uninstallItems, updateItems, null, false, cancellationToken);

    internal async Task Load(
        BeutlApiApplication apiApp,
        string[] installItems,
        string[] uninstallItems,
        string[] updateItems,
        PackageChangesQueue? queue,
        bool fromEditor,
        CancellationToken cancellationToken)
    {
        var queuedInstalls = queue?.GetInstalls().Select(PackageChangesQueue.ToArgument) ?? [];
        var queuedUninstalls = queue?.GetUninstalls().Select(PackageChangesQueue.ToArgument) ?? [];
        List<PackageChangeModel> installViewModels = await ParseUniqueAsync(
            apiApp, installItems, queuedInstalls, PackageChangeAction.Install, fromEditor, cancellationToken);
        List<PackageChangeModel> updateViewModels = await ParseUniqueAsync(
            apiApp, updateItems, [], PackageChangeAction.Update, fromEditor, cancellationToken);
        List<PackageChangeModel> uninstallViewModels = await ParseUniqueAsync(
            apiApp, uninstallItems, queuedUninstalls, PackageChangeAction.Uninstall, fromEditor, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        AddAll(InstallItems, installViewModels);
        AddAll(UpdateItems, updateViewModels);
        AddAll(UninstallItems, uninstallViewModels);
    }

    private static async Task<List<PackageChangeModel>> ParseUniqueAsync(
        BeutlApiApplication apiApp,
        string[] items,
        IEnumerable<string> queuedItems,
        PackageChangeAction action,
        bool fromEditor,
        CancellationToken cancellationToken)
    {
        var viewModels = new List<PackageChangeModel>();
        var ids = new HashSet<string>();
        var requests = items.Select(item => (item, !fromEditor)).Concat(queuedItems.Select(item => (item, false)));
        foreach (var (item, fromCommandLine) in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageChangeModel? itemViewModel = await PackageChangeModel.TryParse(
                apiApp,
                item,
                action,
                fromCommandLine,
                cancellationToken);

            if (itemViewModel != null && ids.Add(itemViewModel.Id))
            {
                viewModels.Add(itemViewModel);
            }
        }

        return viewModels;
    }

    private static void AddAll(ReactiveCollection<PackageChangeModel> target, List<PackageChangeModel> items)
    {
        foreach (PackageChangeModel item in items)
        {
            target.Add(item);
        }
    }
}
