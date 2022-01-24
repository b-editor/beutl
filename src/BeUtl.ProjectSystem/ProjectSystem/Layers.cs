
using BeUtl.Collections;

namespace BeUtl.ProjectSystem;

public sealed class Layers : LogicalList<Layer>
{
    public Layers(ILogicalElement parent)
        : base(parent)
    {
    }
}
