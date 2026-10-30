using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class PreviewGuideAspectRatioTests
{
    [TestCase("9:16", 9, 16)]
    [TestCase(" 2.39 : 1 ", 2.39, 1)]
    [TestCase("1.85", 1.85, 1)]
    [TestCase("4:5", 4, 5)]
    public void Reads_a_ratio(string text, double width, double height)
    {
        Assert.That(PreviewGuideAspectRatio.TryParse(text, out PreviewGuideAspectRatio ratio), Is.True);
        Assert.That(ratio, Is.EqualTo(new PreviewGuideAspectRatio(width, height)));
    }

    [TestCase("")]
    [TestCase("16:")]
    [TestCase(":9")]
    [TestCase("0:1")]
    [TestCase("-4:3")]
    [TestCase("9:16:1")]
    [TestCase("abc")]
    [TestCase("1e3:1")]
    public void Rejects_what_is_not_a_ratio(string text)
    {
        Assert.That(PreviewGuideAspectRatio.TryParse(text, out _), Is.False);
    }

    // A comma separates the ratios, so a decimal comma such as Spanish 2,39 cannot be one.
    [Test]
    public void Reads_numbers_in_the_invariant_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        try
        {
            Assert.That(PreviewGuideAspectRatio.TryParseList("2.39:1, 1:1", out PreviewGuideAspectRatio[] ratios), Is.True);
            Assert.That(ratios, Is.EqualTo(new[] { new PreviewGuideAspectRatio(2.39, 1), new PreviewGuideAspectRatio(1, 1) }));
            Assert.That(ratios[0].ToString(), Is.EqualTo("2.39:1"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Test]
    public void A_list_keeps_the_ratios_it_reads_and_reports_the_rest()
    {
        Assert.That(PreviewGuideAspectRatio.TryParseList("9:16, wide, ,1:1,", out PreviewGuideAspectRatio[] ratios), Is.False);
        Assert.That(ratios, Is.EqualTo(new[] { new PreviewGuideAspectRatio(9, 16), new PreviewGuideAspectRatio(1, 1) }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void An_empty_list_is_valid(string? text)
    {
        Assert.That(PreviewGuideAspectRatio.TryParseList(text, out PreviewGuideAspectRatio[] ratios), Is.True);
        Assert.That(ratios, Is.Empty);
    }

    [Test]
    public void The_default_ratios_all_read()
    {
        var config = new EditorConfig();

        Assert.That(PreviewGuideAspectRatio.TryParseList(config.PreviewGuideAspectRatios.Value, out PreviewGuideAspectRatio[] ratios), Is.True);
        Assert.That(ratios.Select(r => r.ToString()), Is.EqualTo(new[] { "9:16", "1:1", "4:5", "2.39:1" }));
    }

    [Test]
    public void The_guide_settings_read_back()
    {
        var config = new EditorConfig
        {
            IsPreviewGuideEnabled = { Value = true },
            PreviewGuideKinds = { Value = PreviewGuideKinds.RuleOfThirds | PreviewGuideKinds.AspectRatio },
            PreviewGuideColor = { Value = "#FF00E5FF" },
            PreviewGuideOpacity = { Value = 0.5f },
            PreviewGuideGridColumns = { Value = 6 },
            PreviewGuideGridRows = { Value = 2 },
            PreviewGuideAspectRatios = { Value = "9:16" },
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);
        var restored = new EditorConfig();
        CoreSerializer.PopulateFromJsonObject(restored, typeof(EditorConfig), json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.IsPreviewGuideEnabled.Value, Is.True);
            Assert.That(restored.PreviewGuideKinds.Value, Is.EqualTo(PreviewGuideKinds.RuleOfThirds | PreviewGuideKinds.AspectRatio));
            Assert.That(restored.PreviewGuideColor.Value, Is.EqualTo("#FF00E5FF"));
            Assert.That(restored.PreviewGuideOpacity.Value, Is.EqualTo(0.5f));
            Assert.That(restored.PreviewGuideGridColumns.Value, Is.EqualTo(6));
            Assert.That(restored.PreviewGuideGridRows.Value, Is.EqualTo(2));
            Assert.That(restored.PreviewGuideAspectRatios.Value, Is.EqualTo("9:16"));
        });
    }

    [Test]
    public void Out_of_range_counts_and_opacity_are_coerced()
    {
        var config = new EditorConfig
        {
            PreviewGuideOpacity = { Value = 3f },
            PreviewGuideGridColumns = { Value = 0 },
            PreviewGuideGridRows = { Value = 1000 },
        };

        Assert.Multiple(() =>
        {
            Assert.That(config.PreviewGuideOpacity.Value, Is.EqualTo(1f));
            Assert.That(config.PreviewGuideGridColumns.Value, Is.EqualTo(1));
            Assert.That(config.PreviewGuideGridRows.Value, Is.EqualTo(64));
        });
    }
}
