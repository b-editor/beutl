using Beutl.Collections;

namespace Beutl.ProjectSystem;

public sealed class Elements : HierarchicalList<Element>
{
    public Elements(IModifiableHierarchical parent) : base(parent)
    {
    }

    public Elements() : base()
    {
    }
}
