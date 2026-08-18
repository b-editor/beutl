using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record EntitlementsResponse
{
    [JsonPropertyName("plan")] public required string? Plan { get; init; }

    [JsonPropertyName("subscriptionStatus")] public required string? SubscriptionStatus { get; init; }

    [JsonPropertyName("currentPeriodStart")] public required string? CurrentPeriodStart { get; init; }

    [JsonPropertyName("currentPeriodEnd")] public required string? CurrentPeriodEnd { get; init; }

    // A cancellation made in the Stripe customer portal keeps the subscription
    // active until the period ends, so the status alone cannot report it.
    [JsonPropertyName("cancelAtPeriodEnd")] public bool CancelAtPeriodEnd { get; init; }

    [JsonPropertyName("canUseAi")] public required bool CanUseAi { get; init; }

    [JsonPropertyName("balance")] public required AiBalanceResponse Balance { get; init; }

    // The server decides what a client may start without sending its price catalog.
    [JsonPropertyName("availability")]
    public required ImmutableDictionary<string, bool> Availability { get; init; }

    // Per model within each operation. An operation reads as available when any
    // one of its models does, so this is what decides which entries a picker
    // may offer.
    [JsonPropertyName("modelAvailability")]
    public ImmutableDictionary<string, ImmutableDictionary<string, bool>>? ModelAvailability { get; init; }

    internal bool TryNormalize(out EntitlementsResponse? normalized)
    {
        normalized = null;
        if (Balance is null
            || !Balance.TryNormalize(out AiBalanceResponse? balance)
            || Availability is null)
        {
            return false;
        }

        normalized = this with { Balance = balance! };
        return true;
    }
}
