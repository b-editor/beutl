using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiMonthlyUsageResponse
{
    [JsonPropertyName("usedPercent")]
    public required int UsedPercent { get; init; }

    [JsonPropertyName("remainingPercent")]
    public required int RemainingPercent { get; init; }

    [JsonPropertyName("isExhausted")]
    public required bool IsExhausted { get; init; }
}

internal sealed record AiBalanceResponse
{
    [JsonPropertyName("monthlyUsage")]
    public required AiMonthlyUsageResponse MonthlyUsage { get; init; }

    // Exact purchased-credit balance is limited to the account entitlement snapshot.
    [JsonPropertyName("additionalCredits")]
    public required int AdditionalCredits { get; init; }

    [JsonPropertyName("hasAdditionalCreditDebt")]
    public bool HasAdditionalCreditDebt { get; init; }

    internal bool TryNormalize(out AiBalanceResponse? normalized)
    {
        normalized = null;
        if (MonthlyUsage is null
            || MonthlyUsage.UsedPercent is < 0 or > 100
            || MonthlyUsage.RemainingPercent is < 0 or > 100
            || AdditionalCredits < 0)
        {
            return false;
        }

        normalized = this;
        return true;
    }
}
