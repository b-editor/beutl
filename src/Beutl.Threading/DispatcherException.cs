using System.Diagnostics.CodeAnalysis;

namespace Beutl.Threading;

[Serializable]
public class DispatcherException : Exception
{
    [ExcludeFromCodeCoverage]
    public DispatcherException() { }

    public DispatcherException(string message) : base(message) { }

    [ExcludeFromCodeCoverage]
    public DispatcherException(string message, Exception inner) : base(message, inner) { }
}
