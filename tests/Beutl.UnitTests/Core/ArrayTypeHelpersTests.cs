using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

public class ArrayTypeHelpersTests
{
    [Test]
    public void GetElementType_Array_ReturnsElementType()
    {
        Type? element = ArrayTypeHelpers.GetElementType(typeof(int[]));
        Assert.That(element, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void GetElementType_GenericList_ReturnsElementType()
    {
        Type? element = ArrayTypeHelpers.GetElementType(typeof(List<string>));
        Assert.That(element, Is.EqualTo(typeof(string)));
    }

    [Test]
    public void GetElementType_NonEnumerable_ReturnsNull()
    {
        Type? element = ArrayTypeHelpers.GetElementType(typeof(int));
        Assert.That(element, Is.Null);
    }

    [Test]
    public void GetElementType_CachesAfterFirstLookup()
    {
        Type? first = ArrayTypeHelpers.GetElementType(typeof(List<double>));
        Type? second = ArrayTypeHelpers.GetElementType(typeof(List<double>));
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(typeof(double)));
            Assert.That(second, Is.EqualTo(typeof(double)));
        });
    }

    [Test]
    public void GetEntryType_DictionaryType_ReturnsKeyValueTypes()
    {
        var (k, v) = ArrayTypeHelpers.GetEntryType(typeof(Dictionary<string, int>));
        Assert.Multiple(() =>
        {
            Assert.That(k, Is.EqualTo(typeof(string)));
            Assert.That(v, Is.EqualTo(typeof(int)));
        });
    }

    [Test]
    public void GetEntryType_NonDictionary_ReturnsDefault()
    {
        var (k, v) = ArrayTypeHelpers.GetEntryType(typeof(List<string>));
        Assert.Multiple(() =>
        {
            Assert.That(k, Is.Null);
            Assert.That(v, Is.Null);
        });
    }

    [Test]
    public void ConvertArrayType_ToArray_ReturnsTypedArray()
    {
        var output = new List<object?> { 1, 2, 3 };
        object result = ArrayTypeHelpers.ConvertArrayType(output, typeof(int[]), typeof(int));

        Assert.That(result, Is.TypeOf<int[]>());
        Assert.That((int[])result, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void ConvertArrayType_ToList_PopulatesList()
    {
        var output = new List<object?> { "a", "b" };
        object result = ArrayTypeHelpers.ConvertArrayType(
            output,
            typeof(List<string>),
            typeof(string)
        );

        Assert.That(result, Is.InstanceOf<List<string>>());
        Assert.That((List<string>)result, Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ConvertDictionaryType_ToDictionary_PopulatesEntries()
    {
        var output = new List<KeyValuePair<string, object?>> { new("k1", 1), new("k2", 2) };
        object result = ArrayTypeHelpers.ConvertDictionaryType(
            output,
            typeof(Dictionary<string, int>),
            typeof(int)
        );

        Assert.That(result, Is.InstanceOf<Dictionary<string, int>>());
        var dict = (Dictionary<string, int>)result;
        Assert.Multiple(() =>
        {
            Assert.That(dict["k1"], Is.EqualTo(1));
            Assert.That(dict["k2"], Is.EqualTo(2));
        });
    }

    [Test]
    public void ConvertDictionaryType_ToArray_ReturnsKeyValuePairArray()
    {
        var output = new List<KeyValuePair<string, object?>> { new("a", "1"), new("b", "2") };
        object result = ArrayTypeHelpers.ConvertDictionaryType(
            output,
            typeof(KeyValuePair<string, string>[]),
            typeof(string)
        );

        Assert.That(result, Is.TypeOf<KeyValuePair<string, string>[]>());
        var arr = (KeyValuePair<string, string>[])result;
        Assert.That(arr, Has.Length.EqualTo(2));
    }
}
