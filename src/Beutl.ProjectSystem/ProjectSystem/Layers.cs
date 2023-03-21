
using Beutl.Collections;

namespace Beutl.ProjectSystem;

public sealed class Layers : HierarchicalList<Layer>
{
    public Layers(IModifiableHierarchical parent)
        : base(parent)
    {
    }
}
