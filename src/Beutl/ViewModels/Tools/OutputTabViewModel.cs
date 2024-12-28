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
        EditViewModel = editViewModel;
        _outputService = new OutputService(editViewModel);
        CanRemove = SelectedItem
            .SelectMany(x => x?.Context?.IsEncoding?.Not() ?? Observable.Return(false))
            .ToReadOnlyReactivePropertySlim();
        CreateDefaultProfile();
        SelectedItem.Value = Items.FirstOrDefault();
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
            _outputService.AddItem(EditViewModel.Scene.FileName, extension);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception has occurred.");
            e.Handle();
        }
    }

    public void RemoveSelected()
    {
        if (SelectedItem.Value != null)
        {
            Items.Remove(SelectedItem.Value);
        }
    }

    public void Save()
    {
        _outputService.SaveItems();
    }

    public void Dispose()
    {
    }

    public void WriteToJson(JsonObject json)
    {
        _outputService.SaveItems();
    }

    public void ReadFromJson(JsonObject json)
    {
        _outputService.RestoreItems();
        CreateDefaultProfile();
        SelectedItem.Value = Items.FirstOrDefault();
    }

    private void CreateDefaultProfile()
    {
        if (Items.Count != 0) return;

        var ext = OutputService.GetExtensions(EditViewModel.Scene.FileName);
        if (ext.Length == 1)
        {
            AddItem(ext[0]);
        }
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
