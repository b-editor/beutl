namespace Beutl;

[Serializable]
public class HierarchyException : Exception
{
    public HierarchyException() { }

    public HierarchyException(string message) : base(message) { }

    public HierarchyException(string message, Exception inner) : base(message, inner) { }
}
