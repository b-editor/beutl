using Beutl.ProjectSystem;

namespace Beutl.Editor.Services.AI;

/// <summary>Whether the person can run AI generations right now.</summary>
public enum TimelineAiAccess
{
    Ready,
    SignInRequired,
    PlanRequired,

    /// <summary>Signed in, with the plan not known yet; generations stay offered.</summary>
    Loading,
}

/// <summary>
/// What a timeline generation is told before it runs: whether the balance covers it, and
/// in words, how it will be paid for. An unanswered check leaves it affordable, since the
/// service checks again before anything is charged.
/// </summary>
public interface ITimelineAiUsageEstimate : IDisposable
{
    IObservable<bool> CanAfford { get; }

    IObservable<string> Explanation { get; }

    /// <summary>Asks about one request; a duration makes it a video request.</summary>
    void Check(string operationId, string? modelId, int? durationSeconds);
}

/// <summary>
/// What the timeline needs from the application to offer AI generations: the account's
/// access, usage estimates, and the AI tab for anything the popup does not cover.
/// </summary>
public interface ITimelineAiHost
{
    IObservable<TimelineAiAccess> Access { get; }

    /// <summary>Shows the AI tab, where the person can sign in or choose a plan.</summary>
    void OpenAiWorkspace();

    /// <summary>Shows the AI tab's subtitle page with the element's sound as the source.</summary>
    void OpenSubtitles(Element element);

    ITimelineAiUsageEstimate CreateUsageEstimate();

    /// <summary>Where files made for the scene are kept, so they are saved and moved with it.</summary>
    string GetResourceDirectory(Scene scene);
}
