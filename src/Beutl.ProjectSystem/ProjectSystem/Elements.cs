using Beutl.Collections;

namespace Beutl.ProjectSystem;

public sealed class Elements(IModifiableHierarchical parent) : HierarchicalList<Element>(parent);
