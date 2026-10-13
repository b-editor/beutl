using System.Collections.Immutable;
using System.Text.Json;
using Beutl.Api.Clients;

namespace Beutl.UnitTests.Api;

// The server never sends its price catalog. It reports which operations may be
// started and monthly usage as a proportion.
[TestFixture]
public sealed class AiAvailabilityContractTests
{
    private const string BalanceJson = """
        "balance": {
          "monthlyUsage": {
            "usedPercent": 10,
            "remainingPercent": 90,
            "isExhausted": false
          },
          "additionalCredits": 0,
          "hasAdditionalCreditDebt": false
        }
        """;

    [Test]
    public void EntitlementsWithoutAvailability_AreRejectedInsteadOfUsingClientFallbacks()
    {
        string json = $$"""
            {
              "plan": "pro",
              "subscriptionStatus": "active",
              "currentPeriodStart": "2026-08-01T00:00:00Z",
              "currentPeriodEnd": "2026-09-01T00:00:00Z",
              "cancelAtPeriodEnd": false,
              "canUseAi": true,
              {{BalanceJson}}
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EntitlementsResponse>(json));
    }

    [Test]
    public void ServerAvailability_DeserializesIntoAnImmutableMap()
    {
        string json = $$"""
            {
              "plan": "pro",
              "subscriptionStatus": "active",
              "currentPeriodStart": "2026-08-01T00:00:00Z",
              "currentPeriodEnd": "2026-09-01T00:00:00Z",
              "cancelAtPeriodEnd": false,
              "canUseAi": true,
              {{BalanceJson}},
              "availability": {
                "image.generate": true,
                "video.generate": false
              }
            }
            """;

        EntitlementsResponse? result = JsonSerializer.Deserialize<EntitlementsResponse>(json);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Availability,
                Is.InstanceOf<ImmutableDictionary<string, bool>>());
            Assert.That(result.Availability["image.generate"], Is.True);
            Assert.That(result.Availability["video.generate"], Is.False);
        });
    }

    [Test]
    public void AProportionOutsideZeroToOneHundred_FailsClosed()
    {
        EntitlementsResponse response = Entitlements(usedPercent: 120, remainingPercent: 0, additionalCredits: 0);

        Assert.That(response.TryNormalize(out _), Is.False);
    }

    [Test]
    public void ANegativeCreditBalance_FailsClosed()
    {
        EntitlementsResponse response = Entitlements(usedPercent: 0, remainingPercent: 100, additionalCredits: -1);

        Assert.That(response.TryNormalize(out _), Is.False);
    }

    [Test]
    public void AValidResponse_NormalizesSuccessfully()
    {
        EntitlementsResponse response = Entitlements(usedPercent: 40, remainingPercent: 60, additionalCredits: 25) with
        {
            CancelAtPeriodEnd = true,
            Availability = ImmutableDictionary<string, bool>.Empty.Add("image.generate", true),
        };

        Assert.Multiple(() =>
        {
            Assert.That(response.TryNormalize(out EntitlementsResponse? normalized), Is.True);
            Assert.That(normalized!.CancelAtPeriodEnd, Is.True);
            Assert.That(normalized.Balance.AdditionalCredits, Is.EqualTo(25));
        });
    }

    private static EntitlementsResponse Entitlements(int usedPercent, int remainingPercent, int additionalCredits)
        => new()
        {
            Plan = "pro",
            SubscriptionStatus = "active",
            CurrentPeriodStart = null,
            CurrentPeriodEnd = null,
            CanUseAi = true,
            Balance = new AiBalanceResponse
            {
                MonthlyUsage = new AiMonthlyUsageResponse
                {
                    UsedPercent = usedPercent,
                    RemainingPercent = remainingPercent,
                    IsExhausted = remainingPercent == 0,
                },
                AdditionalCredits = additionalCredits,
            },
            Availability = ImmutableDictionary<string, bool>.Empty,
        };
}
