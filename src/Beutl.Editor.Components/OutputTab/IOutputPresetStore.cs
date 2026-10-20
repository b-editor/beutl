namespace Beutl.Editor.Components.OutputTab;

// The saved output presets, which the app keeps because its built-in presets name its encoders.
internal interface IOutputPresetStore
{
    ICoreList<OutputPresetItem> Items { get; }

    void AddItem(IOutputContext context, string name);

    void SaveItems();
}
