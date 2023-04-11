using Beutl.Operation;

namespace Beutl.Models;

public record struct ElementDescription(
    TimeSpan Start,
    TimeSpan Length,
    int Layer,
    string Name = "",
    OperatorRegistry.RegistryItem? InitialOperator = null);
