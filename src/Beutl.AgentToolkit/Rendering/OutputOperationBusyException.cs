using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Rendering;

internal sealed class OutputOperationBusyException : Exception
{
    public OutputOperationBusyException()
        : base("Another workspace operation is in progress, so the output operation cannot start.")
    {
    }

    public string Code => ErrorCode.WorkspaceBusy;
}
