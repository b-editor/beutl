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
    private Beutl.Services.LibraryService _library = null!;

    [SetUp]
    public void SetUpLibrary() => _library = TypeRegistration.CreateLibraryService();

    [TearDown]
    public void DisposeLibrary() => _library.Dispose();

    // A name no installed font can carry, so "unregistered" holds on any machine. Real subfamily
    // names such as "Inter 28pt" are registered families on some systems and cannot serve here.
    private static readonly string UnregisteredFamilyName = $"Beutl Test Missing {Guid.NewGuid():N}";

    [Test]
    public void List_fonts_can_leave_out_typefaces_to_browse_names()
    {
        RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager);

        FontListResponse full = tools.ListFonts().Value!;
        FontListResponse names = tools.ListFonts(includeTypefaces: false).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(names.Families.Select(family => family.Name), Is.EqualTo(full.Families.Select(family => family.Name)));
            Assert.That(names.Families.SelectMany(family => family.Typefaces), Is.Empty);
        });
    }

    [Test]
    public void List_fonts_projects_the_registered_font_registry()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager);

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
                summary!.Typefaces,
                Is.EquivalentTo(TestFonts.Manager.GetTypefaces(sample)
                    .Distinct()
                    .Select(typeface => new FontTypefaceSummary(
                        (int)typeface.Weight,
                        typeface.Style.ToString()))));
        });
    }

    [Test]
    public void List_fonts_typeface_entry_round_trips_through_apply_edit()
    {
        _ = RequireRegisteredFamilies();
        var queryTools = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager);
        FontFamilySummary? family = queryTools.ListFonts().Value!.Families
            .FirstOrDefault(item => item.Typefaces.Count > 0);
        if (family is null)
        {
            Assert.Ignore("No registered font family exposes a selectable typeface in this environment.");
        }

        FontTypefaceSummary typeface = family.Typefaces[0];
        (EditTools editTools, Scene scene, Element element) = CreateSceneWithText();

        ToolResult<ApplyEditResponse> apply = editTools.ApplyEdit(
            patch: FontPatch(element, family.Name, typeface.Weight, typeface.Style),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            TextBlock text = RequireTextBlock(scene);
            Assert.That(text.FontFamily.Value!.Name, Is.EqualTo(family.Name));
            Assert.That((int)text.FontWeight.Value, Is.EqualTo(typeface.Weight));
            Assert.That(text.FontStyle.Value.ToString(), Is.EqualTo(typeface.Style));
        });
    }

    [Test]
    public void Apply_edit_accepts_an_unnamed_numeric_font_weight()
    {
        (EditTools tools, Scene scene, Element element) = CreateSceneWithText();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: FontPatch(element, weight: 1000),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That((int)RequireTextBlock(scene).FontWeight.Value, Is.EqualTo(1000));
        });
    }

    [Test]
    public void List_fonts_filters_by_name_case_insensitively()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        var tools = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager);

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
        var tools = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager);

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
            patch: FontPatch(element, UnregisteredFamilyName),
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
            Assert.That(RequireTextBlock(scene).FontFamily.Value!.Name, Is.EqualTo(UnregisteredFamilyName));
        });
    }

    [Test]
    public void Apply_edit_does_not_warn_for_a_registered_font_family()
    {
        FontFamily[] registered = RequireRegisteredFamilies();
        (EditTools tools, _, Element element) = CreateSceneWithText();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: FontPatch(element, registered[0].Name),
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Validation, Has.None.Matches<ValidationOutcome>(outcome =>
                outcome.Status == ValidationStatus.Warning
                && outcome.Message!.Contains("not installed", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void List_fonts_without_a_session_lists_the_open_projects_bundled_fonts()
    {
        string root = TestWorkspace.CreateDirectory();
        string fontDirectory = Directory.CreateDirectory(Path.Combine(root, "resources", "fonts")).FullName;
        using (Stream input = typeof(FontToolsTests).Assembly.GetManifestResourceStream("ProjectFontFixture.ttf")!)
        using (Stream output = File.Create(Path.Combine(fontDirectory, "custom.ttf")))
            input.CopyTo(output);
        FontManager projectFonts = TestFonts.Manager.Fork();
        projectFonts.LoadProjectFonts(new Project { Uri = new Uri(Path.Combine(root, "project.bep")) });
        const string family = "Beutl Test Variable";
        // As the live endpoint: a manager per request, with no session bound to list_fonts.
        var open = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager,
            openProject: new OpenProject(projectFonts));
        var closed = new QueryTools(new AgentSessionManager(), new SchemaGenerator(_library), TestFonts.Manager,
            openProject: new OpenProject(null));

        Assert.Multiple(() =>
        {
            Assert.That(open.ListFonts(nameFilter: family).Value!.Families.Select(item => item.Name), Does.Contain(family));
            Assert.That(open.ListFonts(nameFilter: family).Value!.Families.Single(item => item.Name == family).Typefaces, Is.Not.Empty);
            Assert.That(closed.ListFonts(nameFilter: family).Value!.Families.Select(item => item.Name), Does.Not.Contain(family));
        });
    }

    private sealed class OpenProject(FontManager? fonts) : IOpenProjectFonts
    {
        public FontManager? Fonts => fonts;
    }

    private static FontFamily[] RequireRegisteredFamilies()
    {
        FontFamily[] families = TestFonts.Manager.FontFamilies.ToArray();
        if (families.Length == 0)
        {
            Assert.Ignore("No fonts are registered in this environment, so font discovery cannot be exercised.");
        }

        return families;
    }

    private static TextBlock RequireTextBlock(Scene scene)
        => (TextBlock)scene.Children.Single().Objects.Single();

    private static JsonObject FontPatch(
        Element element,
        string? familyName = null,
        int? weight = null,
        string? style = null)
    {
        var objectPatch = new JsonObject
        {
            [nameof(CoreObject.Id)] = element.Objects.Single().Id.Value.ToString()
        };
        if (familyName is not null)
        {
            objectPatch[nameof(TextBlock.FontFamily)] = familyName;
        }

        if (weight is not null)
        {
            objectPatch[nameof(TextBlock.FontWeight)] = weight.Value;
        }

        if (style is not null)
        {
            objectPatch[nameof(TextBlock.FontStyle)] = style;
        }

        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                ["Objects"] = new JsonArray(objectPatch)
            })
        };
    }

    private static (EditTools Tools, Scene Scene, Element Element) CreateSceneWithText()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(new TextBlock { Name = { Value = "title" }, Text = { Value = "Launch" } });
        scene.Children.Add(element);

        var session = new AgentToolkitTestSession(scene);
        return (session.CreateEditTools(), scene, element);
    }
}
