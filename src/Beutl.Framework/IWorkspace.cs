using Beutl.Collections;

namespace Beutl.Framework;

public interface IWorkspace : ITopLevel, IDisposable, IStorable
{
    ICoreList<IWorkspaceItem> Items { get; }

    IDictionary<string, string> Variables { get; }

    Version AppVersion { get; }

    Version MinAppVersion { get; }
}
