namespace Beutl.AgentToolkit.Sessions;

public static class EditingSessionExtensions
{
    // A LiveEditor session binds Root/Documents/History to the editor's UI thread; run reads through
    // the dispatcher so an MCP-request-thread read cannot observe the scene mid-mutation by the editor.
    // File sessions dispatch synchronously, so this is a no-op there.
    public static T ReadOnSession<T>(this IEditingSession session, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(read);

        if (session is IEditingSessionDispatcher dispatcher)
        {
            T result = default!;
            dispatcher.Invoke(() => result = read());
            return result;
        }

        return read();
    }

    // Run a mutation on the editor's dispatcher; group/ungroup touch scene state directly rather
    // than through Reconciler.ApplyFromCurrent. File sessions run inline.
    public static void InvokeOnSession(this IEditingSession session, Action action)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(action);

        if (session is IEditingSessionDispatcher dispatcher)
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
