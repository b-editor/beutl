using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class FontToolsTests
{
    // A name no installed font can carry, so "unregistered" holds on any machine. Real subfamily
    // names such as "Inter 28pt" are registered families on some systems and cannot serve here.
    private static readonly string UnregisteredFamilyName = $"Beutl Test Missing {Guid.NewGuid():N}";

    [Test]
    public void List_fonts_projects_the_registered_font_registry()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<FontListResponse> result = tools.ListFonts();

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        FontListResponse response = result.Value!;

        FontFamily sample = registered[0];
        FontFamilySummary? summary = response.Families.FirstOrDefault(item => item.Name == sample.Name);

        Assert.Multiple(() =>
        {
            Assert.That(response.FamilyCount, Is.EqualTo(response.Families.Count));
            Assert.That(
                response.Families.Select(item => item.Name),
                Is.EquivalentTo(registered.Select(family => family.Name).Distinct()));
            Assert.That(summary, Is.Not.Null, $"'{sample.Name}' is registered but missing from the response");
            Assert.That(
                summary!.Weights,
                Is.EquivalentTo(FontManager.Instance.GetTypefaces(sample).Select(t => t.Weight.ToString()).Distinct()));
            Assert.That(
                summary.Styles,
                Is.EquivalentTo(FontManager.Instance.GetTypefaces(sample).Select(t => t.Style.ToString()).Distinct()));
        });
    }

    [Test]
    public void List_fonts_filters_by_name_case_insensitively()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager());

        // Take a substring of a real family name and ask for it in the opposite case, so the
        // assertion fails if the filter is dropped or made ordinal.
        string sampleName = registered[0].Name;
        string fragment = sampleName[..Math.Min(3, sampleName.Length)];

        ToolResult<FontListResponse> lower = tools.ListFonts(fragment.ToLowerInvariant());
        ToolResult<FontListResponse> upper = tools.ListFonts(fragment.ToUpperInvariant());

        Assert.Multiple(() =>
        {
            Assert.That(lower.IsSuccess, Is.True, lower.Error?.Message);
            Assert.That(lower.Value!.Families.Select(item => item.Name), Does.Contain(sampleName));
            Assert.That(
                lower.Value.Families.Select(item => item.Name),
                Is.EquivalentTo(upper.Value!.Families.Select(item => item.Name)));
            Assert.That(
                lower.Value.Families,
                Has.All.Matches<FontFamilySummary>(
                    item => item.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void List_fonts_returns_nothing_for_a_family_that_is_not_registered()
    {
        _ = RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<FontListResponse> result = tools.ListFonts(UnregisteredFamilyName);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Families, Is.Empty);
            Assert.That(result.Value.FamilyCount, Is.Zero);
        });
    }

    [Test]
    public void Apply_edit_warns_by_name_when_a_font_family_is_not_registered()
    {
        _ = RequireRegisteredFamilies();
        (EditTools tools, Scene scene, Element element) = CreateSceneWithText();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: FontFamilyPatch(element, UnregisteredFamilyName),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        Assert.Multiple(() =>
        {
            // A warning, not a rejection: the family may simply not be installed here, and the
            // renderer falls back rather than failing, so the edit must still land.
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(apply.Value.Validation, Has.Some.Matches<ValidationOutcome>(outcome =>
                outcome.Status == ValidationStatus.Warning
                && outcome.Message!.Contains(UnregisteredFamilyName, StringComparison.Ordinal)));
            Assert.That(apply.Value.Validation, Has.Some.Matches<ValidationOutcome>(outcome =>
                outcome.Status == ValidationStatus.Warning
                && outcome.Hint!.Contains("list_fonts", StringComparison.Ordinal)));
            Assert.That(RequireTextBlock(scene).FontFamily.CurrentValue!.Name, Is.EqualTo(UnregisteredFamilyName));
        });
    }

    [Test]
    public void Apply_edit_does_not_warn_for_a_registered_font_family()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        (EditTools tools, _, Element element) = CreateSceneWithText();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: FontFamilyPatch(element, registered[0].Name),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Validation, Has.None.Matches<ValidationOutcome>(outcome =>
                outcome.Status == ValidationStatus.Warning
                && outcome.Message!.Contains("not installed", StringComparison.Ordinal)));
        });
    }

    private static FontFamily[] RequireRegisteredFamilies()
    {
        FontFamily[] families = FontManager.Instance.FontFamilies.ToArray();
        if (families.Length == 0)
        {
            Assert.Ignore("No fonts are registered in this environment, so font discovery cannot be exercised.");
        }

        return families;
    }

    private static TextBlock RequireTextBlock(Scene scene)
        => (TextBlock)scene.Children.Single().Objects.Single();

    private static JsonObject FontFamilyPatch(Element element, string familyName)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Objects.Single().Id.ToString(),
                    [nameof(TextBlock.FontFamily)] = familyName
                })
            })
        };
    }

    private static (EditTools Tools, Scene Scene, Element Element) CreateSceneWithText()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(new TextBlock { Name = "title", Text = { CurrentValue = "Launch" } });
        scene.Children.Add(element);

        var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return (new EditTools(manager), scene, element);
    }
}
