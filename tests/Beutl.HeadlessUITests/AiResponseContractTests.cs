using System.Reflection;
using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

// The server deliberately withholds every balance snapshot from operation
// responses. Account entitlements still show monthly percentages and the exact
// purchased quantity the user asked to see.
[TestFixture]
public sealed class AiResponseContractTests
{
    private static readonly string[] s_forbiddenMembers =
    [
        "UsageUnits",
        "Used",
        "Remaining",
        "AdditionalCreditDebt",
        "Pricing",
        "Units",
    ];

    // Paging carries its own unrelated Limit, so only balance-shaped types are checked
    // for the allowance size.
    private static readonly string[] s_balanceTypes =
    [
        "AiBalance",
        "AiBalanceResponse",
        "AiMonthlyUsage",
        "AiMonthlyUsageResponse",
    ];

    [Test]
    public void AiContractTypes_DoNotExposeUsageCostsOrRawAllowanceAndDebtUnits()
    {
        Assembly assembly = typeof(AiEntitlements).Assembly;
        var offenders = new List<string>();

        foreach (Type type in assembly.GetTypes())
        {
            if (type.Namespace is not ("Beutl.Api.Clients" or "Beutl.Api.Services"))
                continue;
            if (!type.Name.StartsWith("Ai", StringComparison.Ordinal))
                continue;

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                bool forbidden = s_forbiddenMembers.Contains(property.Name, StringComparer.Ordinal)
                    || (property.Name == "Limit"
                        && s_balanceTypes.Contains(type.Name, StringComparer.Ordinal));
                if (forbidden)
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "AI contract types must not carry usage costs or raw balances.");
    }

    [Test]
    public void OperationResponses_DoNotExposeBalanceSnapshots()
    {
        Assembly assembly = typeof(AiEntitlements).Assembly;
        Type entitlementBalance = assembly.GetType(
            "Beutl.Api.Clients.AiBalanceResponse",
            throwOnError: true)!;
        string[] operationResponses =
        [
            "AiImageResponse",
            "AiTranscriptionResponseDto",
            "AiCaptionTranslationResponseDto",
            "CreateAiVideoResponse",
            "AiVideoJobResponse",
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                assembly.GetType("Beutl.Api.Clients.AiOperationBalanceResponse"),
                Is.Null);
            Assert.That(entitlementBalance.GetProperty("AdditionalCredits"), Is.Not.Null);
            foreach (string responseName in operationResponses)
            {
                Type response = assembly.GetType(
                    $"Beutl.Api.Clients.{responseName}",
                    throwOnError: true)!;
                Assert.That(response.GetProperty("Balance"), Is.Null, responseName);
            }
        });
    }
}
