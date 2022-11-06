
using Beutl.Collections;

namespace Beutl.ProjectSystem;

public sealed class Layers : LogicalList<Layer>
{
    public Layers(ILogicalElement parent)
        : base(parent)
    {
    }
}
