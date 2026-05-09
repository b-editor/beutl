namespace Beutl.UnitTests.Core;

public class ChoicesProviderAttributeTests
{
    private sealed class IntChoicesProvider : IChoicesProvider
    {
        public static IReadOnlyList<object> GetChoices() => [1, 2, 3];
    }

    private sealed class NotAProvider;

    [Test]
    public void Constructor_Stores_ProviderType()
    {
        var attr = new ChoicesProviderAttribute(typeof(IntChoicesProvider));
        Assert.That(attr.ProviderType, Is.EqualTo(typeof(IntChoicesProvider)));
    }

    [Test]
    public void Constructor_NonProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ChoicesProviderAttribute(typeof(NotAProvider)));
    }

    [Test]
    public void Provider_GetChoices_ReturnsConfiguredOptions()
    {
        Assert.That(IntChoicesProvider.GetChoices(), Is.EqualTo(new object[] { 1, 2, 3 }));
    }
}
