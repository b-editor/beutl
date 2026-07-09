using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed class ReconcileException : Exception
{
    public ReconcileException(ToolError error)
        : base(error.Message)
    {
        Error = error;
    }

    public ToolError Error { get; }
}
