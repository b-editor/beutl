using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Workspace;

public sealed class DestructiveGuard
{
    public void EnsureOverwriteAllowed(string path, bool confirmed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path) && !confirmed)
        {
            throw new DestructiveIntentException(path, "Overwriting an existing file requires explicit confirmation.");
        }
    }

    public void EnsureDeleteAllowed(bool confirmed, string target)
    {
        if (!confirmed)
        {
            throw new DestructiveIntentException(target, "Deleting content requires explicit confirmation.");
        }
    }
}

public sealed class DestructiveIntentException : Exception
{
    public DestructiveIntentException(string? target, string message)
        : base(message)
    {
        Target = target;
    }

    public string Code => ErrorCode.DestructiveIntent;

    public string? Target { get; }
}
