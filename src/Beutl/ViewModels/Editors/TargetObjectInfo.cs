using Beutl.ProjectSystem;

namespace Beutl.ViewModels.Editors;

public record TargetObjectInfo(
    string DisplayName,
    CoreObject Object,
    Element? OwnerElement
);
