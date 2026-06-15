using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class LegacyTypeNamesTests
{
    [Test]
    public void ElementFullName_MatchesRuntimeType()
    {
        Assert.That(typeof(Element).FullName, Is.EqualTo(LegacyTypeNames.ElementFullName));
    }

    [Test]
    public void SceneDiscriminator_MatchesTypeFormat()
    {
        Assert.That(TypeFormat.ToString(typeof(Scene)), Is.EqualTo(LegacyTypeNames.SceneDiscriminator));
    }

    [Test]
    public void ElementDiscriminator_MatchesTypeFormat()
    {
        Assert.That(TypeFormat.ToString(typeof(Element)), Is.EqualTo(LegacyTypeNames.ElementDiscriminator));
    }
}
