using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.Services.PrimitiveImpls;

// Saves, applies, renames and deletes dock layouts from the palette, without opening the dock layout tab.
// The operations go through DockLayoutViewModel so they report success and failure the way the tab does.
public sealed partial class DockLayoutTabExtension
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
        Command("dockLayout.saveDockLayout", Strings.SaveDockLayoutAs, needsPresets: false, SaveAsync),
        Command("dockLayout.applyDockLayout", Strings.ApplyDockLayout, needsPresets: true, ApplyAsync),
        Command("dockLayout.renameDockLayout", Strings.RenameDockLayout, needsPresets: true, RenameAsync),
        Command("dockLayout.deleteDockLayout", Strings.DeleteDockLayout, needsPresets: true, DeleteAsync),
    ];

    private ContextCommandDefinition Command(
        string id, string displayName, bool needsPresets,
        Func<DockLayoutViewModel, IContextCommandInteraction, Task> execute)
        => new(id, displayName, "", [])
        {
            Scope = ContextCommandScope.Extension,
            CanExecute = execution => execution.EditorContext is EditViewModel && (!needsPresets || PresetService.Items.Count > 0),
            ExecuteAsync = async execution =>
            {
                if (execution is { Interaction: { } interaction, EditorContext: EditViewModel editViewModel })
                {
                    using var layouts = new DockLayoutViewModel(editViewModel, PresetService);
                    await execute(layouts, interaction);
                }
            }
        };

    private static async Task SaveAsync(DockLayoutViewModel layouts, IContextCommandInteraction interaction)
    {
        // Like the tab, saving under an existing name replaces that layout.
        string? name = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = layouts.SuggestName(),
            Validate = CommandPaletteInput.Required
        });
        if (name is not null)
            layouts.Save(name);
    }

    private static async Task ApplyAsync(DockLayoutViewModel layouts, IContextCommandInteraction interaction)
    {
        if (await PickAsync(layouts, interaction) is { } applied)
            layouts.Apply(applied);
    }

    private async Task RenameAsync(DockLayoutViewModel layouts, IContextCommandInteraction interaction)
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
    }

    private static async Task DeleteAsync(DockLayoutViewModel layouts, IContextCommandInteraction interaction)
    {
        if (await PickAsync(layouts, interaction) is { } removed)
            layouts.Remove(removed);
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
