using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.AI;

namespace Beutl.ViewModels.Dialogs;

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

    // Shows the model the recovered request was named for; none when it named none.
    public static void SelectRecoveredModel(AiModelPickerViewModel picker, AiPendingAttempt? recovery)
    {
        if (recovery is null)
            return;
        if (recovery.Model is not { } model)
        {
            picker.Selected.Value = null;
            return;
        }
        AiModelId id = new(model);
        picker.Selected.Value = picker.Options.FirstOrDefault(option => option.Id == id);
    }

    public static bool IsSameAttempt(AiPendingAttempt first, AiPendingAttempt second)
        => first.AccountId == second.AccountId
            && first.Operation == second.Operation
            && first.Fingerprint == second.Fingerprint;

    // Whether settling name settles the recovery on show as well: it is that very request, or
    // the key no longer holds the recovery as pending.
    public static bool IsSettledBy(
        AiRequestKey requestKey,
        AiPendingAttempt recovery,
        AiRequestName name)
        => string.Equals(recovery.Key, name.Key, StringComparison.Ordinal)
            || !requestKey.IsCurrentPending(recovery);

    // Whether a recovered source still stands for the file at path: a durable copy by its file
    // name, a file the person chose by its full path.
    public static bool MatchesPath(
        this AiRequestRecoverySource source,
        string? path)
    {
        if (path is null)
            return false;
        return source.DurableFile is { } durable
            ? string.Equals(Path.GetFileName(path), durable, StringComparison.Ordinal)
            : string.Equals(
                Path.GetFullPath(source.Path ?? string.Empty),
                Path.GetFullPath(path),
                StringComparison.Ordinal);
    }
}
