using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditorNext;

public class ParentChangingEventArgs : EventArgs
{
    public ParentChangingEventArgs(Element? oldParent, Element? newParent)
    {
        NewParent = newParent;
        OldParent = oldParent;
    }

    public Element? NewParent { get; }

    public Element? OldParent { get; }
}