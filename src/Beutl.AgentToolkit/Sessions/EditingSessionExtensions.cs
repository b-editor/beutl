namespace Beutl.AgentToolkit.Sessions;

public static class EditingSessionExtensions
{
    // Live sessions bind Root/Documents/History to the editor's UI thread; file sessions serialize
    // through the session lock.
    public static T ReadOnSession<T>(this IEditingSession session, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(read);

        T result = default!;
        session.Invoke(() => result = read());
        return result;
    }

    // Run a mutation on the session's dispatcher, for tools that change scene state without the reconciler.
    public static void InvokeOnSession(this IEditingSession session, Action action)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(action);

        session.Invoke(action);
    }

    internal static void MarkDirtyIfFileSession(this IEditingSession session)
    {
        if (session is FileEditingSession fileSession)
        {
            fileSession.MarkDirty();
        }
    }
}
