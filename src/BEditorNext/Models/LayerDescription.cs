using BEditorNext.ProjectSystem;

namespace BEditorNext.Models;

public record struct LayerDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    RenderOperationRegistry.RegistryItem? InitialOperation = null,
    string Name = "");
