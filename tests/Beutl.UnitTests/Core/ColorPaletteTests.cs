using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class ColorPaletteTests
{
    private static readonly ImmutableArray<ColorPalette> s_palettes =
    [
        new ColorPalette("Brand", [new PaletteColor(0xFF3366FF, "Primary"), new PaletteColor(0x80FF0000)]),
        new ColorPalette("Empty", []),
    ];

    [Test]
    public void Palettes_read_back_what_they_write()
    {
        JsonArray json = ColorPalette.ToJson(s_palettes);

        ImmutableArray<ColorPalette> read = ColorPalette.ReadList(JsonNode.Parse(json.ToJsonString()));

        Assert.That(read, Is.EqualTo(s_palettes));
    }

    [Test]
    public void A_color_is_written_as_hex_and_an_unnamed_one_without_a_name()
    {
        JsonObject palette = s_palettes[0].ToJson();

        Assert.That(palette.ToJsonString(), Is.EqualTo(
            """{"Name":"Brand","Colors":[{"Color":"#FF3366FF","Name":"Primary"},{"Color":"#80FF0000"}]}"""));
    }

    [Test]
    public void A_read_leaves_out_what_it_cannot_read_and_keeps_the_rest()
    {
        JsonNode? json = JsonNode.Parse("""
            [
              {"Name": "Kept", "Colors": [
                {"Color": "#12345678", "Name": "Eight digits"},
                {"Color": "#ABCDEF"},
                {"Color": "red"},
                {"Color": 5},
                {"Name": "No color"},
                "#FFFFFFFF",
                {"Color": "#FF000000", "Name": ""}
              ]},
              {"Colors": "not a list"},
              42
            ]
            """);

        ImmutableArray<ColorPalette> read = ColorPalette.ReadList(json);

        Assert.That(read, Is.EqualTo(new[]
        {
            new ColorPalette("Kept",
            [
                new PaletteColor(0x12345678, "Eight digits"),
                new PaletteColor(0xFFABCDEF),
                new PaletteColor(0xFF000000),
            ]),
            new ColorPalette(string.Empty, []),
        }));
    }

    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("\"palettes\"")]
    public void Anything_but_a_list_reads_as_no_palettes(string? text)
    {
        Assert.That(ColorPalette.ReadList(text is null ? null : JsonNode.Parse(text)), Is.Empty);
    }

    [TestCase("#FF102030", 0xFF102030u)]
    [TestCase("#102030", 0xFF102030u)]
    [TestCase("#00ffffff", 0x00FFFFFFu)]
    public void Hex_colors_parse(string text, uint expected)
    {
        Assert.That(ColorPalette.TryParseArgb(text, out uint argb), Is.True);
        Assert.That(argb, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("FF102030")]
    [TestCase("#1020")]
    [TestCase("#-1020304")]
    [TestCase("#GG102030")]
    [TestCase(" #102030")]
    public void Other_text_does_not_parse_as_a_color(string text)
    {
        Assert.That(ColorPalette.TryParseArgb(text, out _), Is.False);
    }

    [Test]
    public void Edits_return_new_palettes_and_leave_the_old_one()
    {
        ColorPalette palette = s_palettes[0];

        ColorPalette edited = palette
            .AddColor(new PaletteColor(0xFF00FF00, "Green"))
            .RemoveColor(1)
            .SetColor(0, new PaletteColor(0xFF000000, "Black"))
            .WithName("Renamed");

        Assert.That(edited, Is.EqualTo(new ColorPalette("Renamed",
            [new PaletteColor(0xFF000000, "Black"), new PaletteColor(0xFF00FF00, "Green")])));
        Assert.That(palette, Is.EqualTo(new ColorPalette("Brand",
            [new PaletteColor(0xFF3366FF, "Primary"), new PaletteColor(0x80FF0000)])));
    }

    [Test]
    public void A_project_keeps_its_palettes_through_a_save()
    {
        var project = new Project();
        project.ColorPalettes.Value = s_palettes;

        JsonObject json = CoreSerializer.SerializeToJsonObject(project);
        var read = (Project)CoreSerializer.DeserializeFromJsonObject(json, typeof(Project));

        Assert.That(read.ColorPalettes.Value, Is.EqualTo(s_palettes));
    }

    [Test]
    public void A_project_without_palettes_writes_nothing_for_them()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Project());

        Assert.That(json.ContainsKey(nameof(Project.ColorPalettes)), Is.False);
    }
}
