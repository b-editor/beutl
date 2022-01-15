using System.Collections.ObjectModel;

namespace BeUtl.Collections;

internal sealed class ElementList : LogicalList<Element>, IElementList
{
    public ElementList(ILogicalElement parent) : base(parent)
    {
    }
}
