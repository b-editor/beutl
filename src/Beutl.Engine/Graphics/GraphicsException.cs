using System.Runtime.Serialization;

namespace Beutl.Graphics;

[Serializable]
public sealed class GraphicsException : Exception
{
    public GraphicsException()
    {
    }

    public GraphicsException(string message)
        : base(message)
    {
    }

    public GraphicsException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
