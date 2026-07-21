using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class UnloadDiagnosticsReportTests
{
    private static UnloadDiagnosticsReport CreateReport(
        IReadOnlyList<UnloadDiagnosticsObjectGroup>? groups = null,
        int total = 0,
        IReadOnlyList<UnloadDiagnosticsRootPath>? rootPaths = null,
        IReadOnlyList<UnloadDiagnosticsThreadStack>? threads = null,
        bool truncated = false)
    {
        return new UnloadDiagnosticsReport(
            "MyPlugin",
            ["MyPlugin", "MyPlugin.Extra"],
            total,
            groups ?? [],
            rootPaths ?? [],
            threads ?? [],
            truncated);
    }

    [Test]
    public void SurvivingTypes_AreSortedByCountDescending_RegardlessOfInputOrder()
    {
        UnloadDiagnosticsReport report = CreateReport(
        [
            new UnloadDiagnosticsObjectGroup("A.Small", "MyPlugin", 1),
            new UnloadDiagnosticsObjectGroup("B.Large", "MyPlugin", 50),
            new UnloadDiagnosticsObjectGroup("C.Medium", "MyPlugin", 10),
        ]);

        Assert.That(report.SurvivingTypes.Select(x => x.TypeName), Is.EqualTo(new[] { "B.Large", "C.Medium", "A.Small" }));
    }

    [Test]
    public void ObjectGroups_KeepSameTypeNameFromDifferentAssembliesDistinct()
    {
        var counts = new Dictionary<(string Assembly, string TypeName), int>();
        ClrmdLoadContextUnloadDiagnostics.CountSurvivor(counts, "Plugin.A", "Shared.Ns.Foo");
        ClrmdLoadContextUnloadDiagnostics.CountSurvivor(counts, "Plugin.B", "Shared.Ns.Foo");
        ClrmdLoadContextUnloadDiagnostics.CountSurvivor(counts, "Plugin.A", "Shared.Ns.Foo");

        List<UnloadDiagnosticsObjectGroup> groups = ClrmdLoadContextUnloadDiagnostics.ToObjectGroups(counts);

        Assert.Multiple(() =>
        {
            // The same type name from two assemblies must not be merged into one group with a single assembly label.
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups, Has.One.Matches<UnloadDiagnosticsObjectGroup>(
                g => g.AssemblyName == "Plugin.A" && g.TypeName == "Shared.Ns.Foo" && g.Count == 2));
            Assert.That(groups, Has.One.Matches<UnloadDiagnosticsObjectGroup>(
                g => g.AssemblyName == "Plugin.B" && g.TypeName == "Shared.Ns.Foo" && g.Count == 1));
        });
    }

    [Test]
    public void BuildSummary_ListsTopFiveTypesAndCounts()
    {
        UnloadDiagnosticsObjectGroup[] groups =
        [
            new("T1", "MyPlugin", 6),
            new("T2", "MyPlugin", 5),
            new("T3", "MyPlugin", 4),
            new("T4", "MyPlugin", 3),
            new("T5", "MyPlugin", 2),
            new("T6", "MyPlugin", 1),
        ];
        UnloadDiagnosticsReport report = CreateReport(groups, total: 21);

        string summary = report.BuildSummary();

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("MyPlugin"));
            Assert.That(summary, Does.Contain("21 live object(s)"));
            Assert.That(summary, Does.Contain("T1 x6"));
            Assert.That(summary, Does.Contain("T5 x2"));
            // Only the top five appear in the one-line summary.
            Assert.That(summary, Does.Not.Contain("T6 x1"));
        });
    }

    [Test]
    public void BuildReport_IncludesAllSections_WithRootPathAndStack()
    {
        UnloadDiagnosticsReport report = CreateReport(
            groups: [new UnloadDiagnosticsObjectGroup("MyPlugin.Leaky", "MyPlugin", 3)],
            total: 3,
            rootPaths:
            [
                new UnloadDiagnosticsRootPath("MyPlugin.Leaky",
                    ["StaticVar -> Core.Registry (0x1)", "field _handler -> MyPlugin.Leaky (0x2)"]),
            ],
            threads:
            [
                new UnloadDiagnosticsThreadStack(1, 4242, ["MyPlugin.Leaky.Run()", "System.Threading.Thread.Loop()"]),
            ]);

        string text = report.BuildReport();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Package: MyPlugin"));
            Assert.That(text, Does.Contain("Surviving objects by type"));
            Assert.That(text, Does.Contain("MyPlugin.Leaky"));
            Assert.That(text, Does.Contain("GC root paths"));
            Assert.That(text, Does.Contain("field _handler -> MyPlugin.Leaky (0x2)"));
            Assert.That(text, Does.Contain("Managed thread stacks"));
            Assert.That(text, Does.Contain("os=4242"));
            Assert.That(text, Does.Contain("at MyPlugin.Leaky.Run()"));
        });
    }

    [Test]
    public void BuildReport_ReportsTruncation_WhenBudgetExceeded()
    {
        UnloadDiagnosticsReport report = CreateReport(truncated: true);

        Assert.That(report.BuildReport(), Does.Contain("budget exceeded"));
    }

    [Test]
    public void BuildReport_HandlesEmptyData_WithoutThrowing()
    {
        UnloadDiagnosticsReport report = CreateReport();

        string text = report.BuildReport();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Surviving objects: 0"));
            Assert.That(text, Does.Contain("(no root path captured)"));
            Assert.That(report.BuildSummary(), Does.Contain("Top types: (none)"));
        });
    }
}
