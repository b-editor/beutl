namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The steps every paid AI request passes before it may start. Each one still lets through a
/// request that already holds a name, because the server answers a repeated name from the job
/// that name made.
/// </summary>
internal static class AiStartGate
{
    /// <summary>
    /// Allows a start the balance covers, or one that holds a name already handed out: the server
    /// answers a repeat with the job that name made before it looks at the balance, so the request
    /// that spent the last of it is exactly the one that must stay collectable.
    /// </summary>
    public static IObservable<bool> WhenAffordable(
        this IObservable<bool> canStart,
        IObservable<bool> canAfford,
        IObservable<bool> holdsName)
        => canStart.CombineLatest(
            canAfford,
            holdsName,
            (can, affordable, outstanding) => can && (affordable || outstanding));

    /// <summary>
    /// Refuses a start once every model the operation registered was ruled out, since a new
    /// request would be refused however it is shaped; a name already handed out is still answered
    /// from the job it made, whatever the catalog says now.
    /// </summary>
    public static IObservable<bool> WhenSomeModelUsable(
        this IObservable<bool> canStart,
        IObservable<bool> offersNothingUsable,
        IObservable<bool> holdsName)
        => canStart.CombineLatest(
            offersNothingUsable,
            holdsName,
            (can, nothingUsable, outstanding) => can && (!nothingUsable || outstanding));

    /// <summary>
    /// Waits for the model list: until it has been asked for, a request would name no model and
    /// run on the server's default, which may cost more than what the dialog was about to offer.
    /// </summary>
    public static IObservable<bool> WhenModelsLoaded(
        this IObservable<bool> canStart,
        IObservable<bool> isLoaded)
        => canStart.CombineLatest(isLoaded, (can, loaded) => can && loaded);
}
