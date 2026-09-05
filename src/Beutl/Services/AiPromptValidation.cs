namespace Beutl.Services;

internal static class AiPromptValidation
{
    /// <summary>
    /// Repeats a prompt's validation message, but only once the person has typed
    /// something. A tab that was just opened has an empty prompt and therefore an
    /// error, yet nothing has gone wrong: showing it reads as a complaint about
    /// work not started.
    /// </summary>
    public static IObservable<string?> WhileTyping(
        IObservable<string?> validationError,
        params IObservable<string>[] promptParts)
    {
        ArgumentNullException.ThrowIfNull(validationError);
        ArgumentNullException.ThrowIfNull(promptParts);

        IObservable<bool> hasTyped = promptParts
            .Merge()
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(_ => true)
            .Take(1)
            .StartWith(false);

        return validationError.CombineLatest(hasTyped, (error, typed) => typed ? error : null);
    }
}
