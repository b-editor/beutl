using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ElementTests
{
    [Test]
    public void IsEnabled_DefaultsToTrue()
    {
        var element = new Element();

        Assert.That(element.IsEnabled, Is.True);
    }

    [Test]
    public void IsEnabled_ChangeRaisesEditedEvent()
    {
        var element = new Element();
        int invocationCount = 0;
        element.Edited += (_, _) => invocationCount++;

        element.IsEnabled = false;

        Assert.That(invocationCount, Is.EqualTo(1));
    }

    [Test]
    public void IsEnabled_SetSameValueDoesNotRaiseEdited()
    {
        var element = new Element { IsEnabled = true };
        int invocationCount = 0;
        element.Edited += (_, _) => invocationCount++;

        element.IsEnabled = true;

        Assert.That(invocationCount, Is.EqualTo(0));
    }
}
