using Beutl.Operation;

namespace Beutl.Models;

public record struct LayerDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    string Name = "",
    OperatorRegistry.RegistryItem? InitialOperator = null);
