using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class FontFamilySearchQueryTests
{
    [TestCase("noto sans", "Noto Sans JP", true)]
    [TestCase("sans noto", "Noto Sans JP", true)]
    [TestCase("noto sans", "Noto Serif JP", false)]
    [TestCase("noto sans", "DejaVu Sans", false)]
    public void Every_word_has_to_match(string query, string name, bool matches)
    {
        Assert.That(Matches(query, name), Is.EqualTo(matches));
    }

    [TestCase("ＲＯＢＯＴＯ", "Roboto")]
    [TestCase("roboto", "ＲＯＢＯＴＯ")]
    [TestCase("ms ごしっく", "ＭＳ ゴシック")]
    [TestCase("ｺﾞｼｯｸ", "游ゴシック")]
    [TestCase("noto　sans", "Noto Sans")]
    public void Case_width_kana_and_ideographic_spaces_do_not_matter(string query, string name)
    {
        Assert.That(Matches(query, name), Is.True);
    }

    [TestCase("notosans", "Noto Sans JP")]
    [TestCase("sourcehan", "Source Han Sans")]
    [TestCase("n-r", "UD デジタル 教科書体 N-R")]
    [TestCase("デジタル教科書体", "UD デジタル 教科書体 N-R")]
    public void A_word_matches_across_spaces_and_hyphens(string query, string name)
    {
        Assert.That(Matches(query, name), Is.True);
    }

    [Test]
    public void A_family_matches_through_any_of_its_names()
    {
        var key = new FontFamilySearchKey(["Yu Gothic", "游ゴシック"]);

        Assert.Multiple(() =>
        {
            Assert.That(FontFamilySearchQuery.Parse("游ゴシック").Score(key), Is.Not.EqualTo(FontFamilySearchQuery.NoMatch));
            Assert.That(FontFamilySearchQuery.Parse("yu goth").Score(key), Is.Not.EqualTo(FontFamilySearchQuery.NoMatch));
        });
    }

    [Test]
    public void A_whole_name_ranks_above_a_prefix_above_word_starts_above_the_rest()
    {
        var query = FontFamilySearchQuery.Parse("sans");
        string[] names = ["Opensans", "Noto Sans", "Sans Forgetica", "Sans"];

        string[] ranked = [.. names.OrderByDescending(name => query.Score(new FontFamilySearchKey([name])))];

        Assert.That(ranked, Is.EqualTo(new[] { "Sans", "Sans Forgetica", "Noto Sans", "Opensans" }));
    }

    [Test]
    public void The_query_without_its_spaces_can_still_be_a_whole_name()
    {
        var query = FontFamilySearchQuery.Parse("notosansjp");

        Assert.That(query.Score(new FontFamilySearchKey(["Noto Sans JP"])),
            Is.GreaterThan(query.Score(new FontFamilySearchKey(["Noto Sans JP Black"]))));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" 　 ")]
    public void A_blank_query_is_empty(string? text)
    {
        Assert.That(FontFamilySearchQuery.Parse(text).IsEmpty, Is.True);
    }

    [Test]
    public void A_name_with_a_lone_surrogate_is_still_searchable()
    {
        var key = new FontFamilySearchKey(["Broken \uD800 Sans"]);

        Assert.That(FontFamilySearchQuery.Parse("broken").Score(key), Is.Not.EqualTo(FontFamilySearchQuery.NoMatch));
    }

    private static bool Matches(string query, string name)
    {
        return FontFamilySearchQuery.Parse(query).Score(new FontFamilySearchKey([name]))
            != FontFamilySearchQuery.NoMatch;
    }
}
