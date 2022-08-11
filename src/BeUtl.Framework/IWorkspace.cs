using BeUtl.Collections;

namespace BeUtl.Framework;

public interface IWorkspace : ITopLevel, IDisposable, IStorable
{
    ICoreList<IWorkspaceItem> Items { get; }

    IDictionary<string, string> Variables { get; }

    Version AppVersion { get; }

    Version MinAppVersion { get; }
}
