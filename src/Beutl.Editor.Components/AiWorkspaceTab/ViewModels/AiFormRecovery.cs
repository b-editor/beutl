using System.Collections.ObjectModel;
using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Services;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>
/// What the image generation, image edit and video generation dialogs do alike to bring back a
/// request whose outcome is not known yet, and to let go of it when the account changes.
/// </summary>
internal static class AiFormRecovery
{
    /// <summary>
    /// Switches <paramref name="identity"/> to the new account, clearing the form with
    /// <paramref name="clear"/>, and then runs <paramref name="recover"/>. Off the UI thread both
    /// are posted there, and a clear that fails is handed to <paramref name="reportClearFailure"/>.
    /// </summary>
    public static void SwitchIdentity(
        IdentityOperationLifetime identity,
        Action clear,
        Action recover,
        Action<Exception> reportClearFailure)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            identity.SwitchDeferred(
                action => Dispatcher.UIThread.Post(() => RunDeferredClear(action, reportClearFailure)),
                clear,
                recover);
            return;
        }

        identity.Switch(clear);
        recover();
    }

    private static void RunDeferredClear(Action clear, Action<Exception> reportClearFailure)
    {
        try
        {
            clear();
        }
        catch (Exception ex)
        {
            reportClearFailure(ex);
        }
    }

    public static IDisposable RememberChoice<T>(
        this IObservable<T> source,
        Func<bool> isApplying,
        Action<T> remember)
        => source.Subscribe(value =>
        {
            if (!isApplying())
                remember(value);
        });

    public static void Replace<T>(this ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }
}
