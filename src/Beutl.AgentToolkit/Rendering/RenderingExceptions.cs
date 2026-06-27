using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Rendering;

public sealed class RenderingUnavailableException : Exception
{
    public RenderingUnavailableException(string message)
        : base(message)
    {
    }

    public string Code => ErrorCode.RenderingUnavailable;
}

public sealed class CodecUnavailableException : Exception
{
    public CodecUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public string Code => ErrorCode.CodecUnavailable;
}
