using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tools;

public sealed partial class RenderTools
{
    internal Scene RequireSceneSnapshot()
    {
        IEditingSession session = sessions.RequireSession();
        return CreateSceneSnapshot(session);
    }

    internal static Scene CreateSceneSnapshot(IEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.ReadOnSession(() =>
        {
            if (session.Root is not Scene scene)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    "The current editing session is not attached to a scene."));
            }

            // Renders and analyzers run after ReadOnSession releases the dispatch lock, so a
            // concurrent apply_edit can mutate the live scene mid-render for ANY session source;
            // every snapshot must be an isolated clone.
            return SceneSnapshot.Create(scene);
        });
    }
}
