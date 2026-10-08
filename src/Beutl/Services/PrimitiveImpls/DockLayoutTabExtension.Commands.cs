using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.Services.PrimitiveImpls;

// Saves, applies, renames and deletes dock layouts from the palette, without opening the dock layout tab.
// The operations go through DockLayoutViewModel so they report success and failure the way the tab does.
public sealed partial class DockLayoutTabExtension : IContextCommandHandler
{
    private readonly DockLayoutPresetService? _presetService;

    public DockLayoutTabExtension()
    {
    }

    internal DockLayoutTabExtension(DockLayoutPresetService presetService)
    {
        _presetService = presetService;
    }

    // Resolved on use so that creating Instance does not load the presets.
    private DockLayoutPresetService PresetService => _presetService ?? DockLayoutPresetService.Instance;

    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("SaveDockLayout", Strings.SaveDockLayoutAs, "", []) { Scope = ContextCommandScope.Extension },
        new("ApplyDockLayout", Strings.ApplyDockLayout, "", []) { Scope = ContextCommandScope.Extension },
        new("RenameDockLayout", Strings.RenameDockLayout, "", []) { Scope = ContextCommandScope.Extension },
        new("DeleteDockLayout", Strings.DeleteDockLayout, "", []) { Scope = ContextCommandScope.Extension },
    ];

    public bool CanExecute(ContextCommandExecution execution)
    {
        if (execution.EditorContext is not EditViewModel) return false;

        return execution.CommandName switch
        {
            "SaveDockLayout" => true,
            "ApplyDockLayout" or "RenameDockLayout" or "DeleteDockLayout" => PresetService.Items.Count > 0,
            _ => false,
        };
    }

    public async Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (execution.Interaction is not { } interaction
            || execution.EditorContext is not EditViewModel editViewModel)
        {
            return;
        }

        using var layouts = new DockLayoutViewModel(editViewModel, PresetService);
        switch (execution.CommandName)
        {
            case "SaveDockLayout":
                {
                    // Like the tab, saving under an existing name replaces that layout.
                    string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
                    {
                        Value = layouts.SuggestName(),
                        Validate = CommandPaletteInput.Required
                    });
                    if (name is not null)
                        layouts.Save(name);
                    break;
                }

            case "ApplyDockLayout":
                if (await PickAsync(layouts, interaction) is { } applied)
                    layouts.Apply(applied);
                break;

            case "RenameDockLayout":
                {
                    if (await PickAsync(layouts, interaction) is not { } item) return;

                    string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
                    {
                        Value = item.Name.Value,
                        Validate = value => CommandPaletteInput.Required(value)
                            ?? (PresetService.Find(value) is { } other && !ReferenceEquals(other, item)
                                ? Strings.CommandPalette_NameInUse
                                : null)
                    });
                    if (name is not null)
                        layouts.Rename(item, name);
                    break;
                }

            case "DeleteDockLayout":
                if (await PickAsync(layouts, interaction) is { } removed)
                    layouts.Remove(removed);
                break;
        }
    }

    private static async Task<DockLayoutPresetItem?> PickAsync(
        DockLayoutViewModel layouts, IContextCommandInteraction interaction)
    {
        ContextCommandPickItem<DockLayoutPresetItem>[] items = layouts.Items
            .Select(item => new ContextCommandPickItem<DockLayoutPresetItem>(item.Name.Value, item))
            .ToArray();
        DockLayoutPresetItem? picked = (await interaction.ShowQuickPickAsync(items))?.Value;
        // The list is stored outside the scene, so another window may have removed the layout meanwhile.
        return picked is not null && layouts.Items.Contains(picked) ? picked : null;
    }
}
