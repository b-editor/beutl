namespace Beutl.Api.Services;

/// <summary>
/// Composes extension registrations that come in an Add phase (0) followed by a Replace phase (1).
/// </summary>
/// <remarks>
/// Each pass starts from a fresh state and applies every candidate not yet rejected, phase by phase in candidate
/// order. A failure can stem from another candidate's registrations, so a failing pass rejects only the first
/// candidate that failed in the latest failing phase and re-evaluates the rest in the next pass.
/// </remarks>
internal static class TwoPhaseComposition
{
    private const int PhaseCount = 2;

    public static TState Compose<TCandidate, TKey, TState>(
        IReadOnlyList<TCandidate> candidates,
        Func<TCandidate, TKey> getKey,
        Dictionary<TKey, Exception> rejected,
        Func<TState> createState,
        Func<TState, TCandidate, int, TState> applyPhase)
        where TKey : notnull
    {
        while (true)
        {
            TState state = createState();
            var newFailures = new Dictionary<TKey, (Exception Exception, int Phase)>(rejected.Comparer);
            for (int phaseIndex = 0; phaseIndex < PhaseCount; phaseIndex++)
            {
                foreach (TCandidate candidate in candidates)
                {
                    TKey key = getKey(candidate);
                    if (rejected.ContainsKey(key) || newFailures.ContainsKey(key))
                        continue;

                    try
                    {
                        state = applyPhase(state, candidate, phaseIndex);
                    }
                    catch (Exception ex)
                    {
                        newFailures.Add(key, (ex, phaseIndex));
                    }
                }
            }

            if (newFailures.Count == 0)
                return state;

            int latestFailedPhase = newFailures.Values.Max(failure => failure.Phase);
            KeyValuePair<TKey, (Exception Exception, int Phase)> first =
                newFailures.First(failure => failure.Value.Phase == latestFailedPhase);
            rejected.TryAdd(first.Key, first.Value.Exception);
        }
    }
}
