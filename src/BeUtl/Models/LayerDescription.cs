using BeUtl.ProjectSystem;
using BeUtl.Streaming;

namespace BeUtl.Models;

public record struct LayerDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    LayerOperationRegistry.RegistryItem? InitialOperation = null,
    string Name = "",
    OperatorRegistry.RegistryItem? InitialOperator = null);
