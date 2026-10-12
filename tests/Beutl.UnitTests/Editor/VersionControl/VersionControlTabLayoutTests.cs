using Beutl.Editor.Components.VersionControlTab.Views;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlTabLayoutTests
{
    [TestCase(0, true)]
    [TestCase(599.999, true)]
    [TestCase(600, false)]
    [TestCase(900, false)]
    public void Width_selects_the_expected_layout(double width, bool expectedNarrow)
    {
        Assert.That(
            VersionControlTabLayout.IsNarrow(width),
            Is.EqualTo(expectedNarrow));
    }
}
