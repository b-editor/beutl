using Beutl.ProjectSystem;
using NUnit.Framework.Legacy;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ElementTests
{
    [Test]
    public void IsEnabled_DefaultsToTrue()
    {
        var element = new Element();

        ClassicAssert.IsTrue(element.IsEnabled);
    }

    [Test]
    public void IsEnabled_ChangeRaisesEditedEvent()
    {
        var element = new Element();
        int invocationCount = 0;
        element.Edited += (_, _) => invocationCount++;

        element.IsEnabled = false;

        ClassicAssert.AreEqual(1, invocationCount);
    }

    [Test]
    public void IsEnabled_SetSameValueDoesNotRaiseEdited()
    {
        var element = new Element { IsEnabled = true };
        int invocationCount = 0;
        element.Edited += (_, _) => invocationCount++;

        element.IsEnabled = true;

        ClassicAssert.AreEqual(0, invocationCount);
    }
}
