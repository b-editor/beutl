using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public class OutputTabViewModel : IToolContext
{
    private readonly OutputService _outputService;
    private readonly ILogger _logger = Log.CreateLogger<OutputTabViewModel>();

    public OutputTabViewModel(EditViewModel editViewModel)
    {
        _logger.LogInformation("Initializing OutputTabViewModel.");
        EditViewModel = editViewModel;
        _outputService = new OutputService(editViewModel);
        CanRemove = SelectedItem
            .SelectMany(x => x?.Context?.IsEncoding?.Not() ?? Observable.Return(false))
            .ToReadOnlyReactivePropertySlim();
        CreateDefaultProfile();
        SelectedItem.Value = Items.FirstOrDefault();
        _logger.LogInformation("OutputTabViewModel initialized.");
    }

    public EditViewModel EditViewModel { get; }

    public ToolTabExtension Extension => OutputTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; }
        = new ReactiveProperty<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; }
        = new ReactiveProperty<ToolTabExtension.TabDisplayMode>();

    public string Header => Strings.Output;

    public ICoreList<OutputProfileItem> Items => _outputService.Items;

    public IReactiveProperty<OutputProfileItem?> SelectedItem => _outputService.SelectedItem;

    public ReadOnlyReactivePropertySlim<bool> CanRemove { get; }

    public void AddItem(OutputExtension extension)
    {
        try
        {
            _logger.LogInformation("Adding item with extension: {ExtensionName}", extension.Name);
            _outputService.AddItem(EditViewModel.Scene.FileName, extension);
            _logger.LogInformation("Item added successfully.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception occurred while adding an item.");
            e.Handle();
        }
    }

    public void RemoveSelected()
    {
        if (SelectedItem.Value != null)
        {
            _logger.LogInformation("Removing selected item: {ItemName}", SelectedItem.Value.Context.Name.Value);
            Items.Remove(SelectedItem.Value);
            _logger.LogInformation("Selected item removed successfully.");
        }
    }

    public void Save()
    {
        _logger.LogInformation("Saving items.");
        _outputService.SaveItems();
        _logger.LogInformation("Items saved successfully.");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputTabViewModel.");
    }

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing items to JSON.");
        _outputService.SaveItems();
        _logger.LogInformation("Items written to JSON successfully.");
    }

    public void ReadFromJson(JsonObject json)
    {
        try
        {
            _logger.LogInformation("Reading items from JSON.");
            _outputService.RestoreItems();
            CreateDefaultProfile();
            SelectedItem.Value = Items.FirstOrDefault();
            _logger.LogInformation("Items read from JSON successfully.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception occurred while reading items from JSON.");
        }
    }

    private void CreateDefaultProfile()
    {
        if (Items.Count != 0) return;

        _logger.LogInformation("Creating default profile.");
        var ext = OutputService.GetExtensions(EditViewModel.Scene.FileName);
        if (ext.Length == 1)
        {
            AddItem(ext[0]);
            _logger.LogInformation("Default profile created with extension: {ExtensionName}", ext[0].Name);
        }
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
