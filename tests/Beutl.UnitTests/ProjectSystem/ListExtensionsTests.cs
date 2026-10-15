namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ListExtensionsTests
{
    [Test]
    public void OrderedAdd_InsertsBeforeFirstLargerOrEqualKey()
    {
        var list = new List<int> { 1, 3, 5 };

        list.OrderedAdd(2, x => x);

        Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 5 }));
    }

    [Test]
    public void OrderedAdd_AppendsWhenLargestKey()
    {
        var list = new List<int> { 1, 2, 3 };

        list.OrderedAdd(99, x => x);

        Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 99 }));
    }

    [Test]
    public void OrderedAdd_PrependsWhenSmallestKey()
    {
        var list = new List<int> { 5, 6, 7 };

        list.OrderedAdd(1, x => x);

        Assert.That(list, Is.EqualTo(new[] { 1, 5, 6, 7 }));
    }

    [Test]
    public void OrderedAdd_EqualKeyInsertedBeforeExisting()
    {
        var list = new List<(int Key, string Tag)>
        {
            (1, "a"),
            (3, "b"),
        };

        list.OrderedAdd<(int Key, string Tag), int>((3, "c"), x => x.Key);

        Assert.That(list[0], Is.EqualTo((1, "a")));
        Assert.That(list[1], Is.EqualTo((3, "c")));
        Assert.That(list[2], Is.EqualTo((3, "b")));
    }

    [Test]
    public void OrderedAdd_EmptyListAppends()
    {
        var list = new List<int>();

        list.OrderedAdd(42, x => x);

        Assert.That(list, Is.EqualTo(new[] { 42 }));
    }

    [Test]
    public void OrderedAdd_NullList_Throws()
    {
        List<int>? list = null;

        Assert.Throws<ArgumentNullException>(() => list!.OrderedAdd(1, x => x));
    }

    [Test]
    public void OrderedAdd_NullKeySelector_Throws()
    {
        var list = new List<int>();

        Assert.Throws<ArgumentNullException>(() => list.OrderedAdd(1, (Func<int, int>)null!));
    }
}
