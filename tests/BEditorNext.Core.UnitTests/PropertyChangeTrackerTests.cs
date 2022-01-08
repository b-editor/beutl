
using NUnit.Framework;

namespace BEditorNext.Core.UnitTests;

public class PropertyChangeTrackerTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
        var elm_1 = new TestElement() { String = "" };
        var elm = new TestElement()
        {
            Children =
            {
                elm_1
            }
        };
        var array = new Element[] { elm };
        const string Foo = "Foo";
        const string Bar = "Bar";

        elm.String = Foo;
        using (var tracker = new PropertyChangeTracker(array, 0))
        {
            elm.String = Bar;
            elm_1.String = "Depth_1";

            IRecordableCommand command = tracker.ToCommand();

            command.Undo();

            Assert.AreEqual(Foo, elm.String);
            Assert.AreEqual("Depth_1", elm_1.String);

            command.Redo();
            Assert.AreEqual(Bar, elm.String);
            Assert.AreEqual("Depth_1", elm_1.String);
        }
    }
}

public class TestElement : Element
{
    public static readonly CoreProperty<string> StringProperty;

    static TestElement()
    {
        StringProperty = ConfigureProperty<string, TestElement>("String")
            .DefaultValue(string.Empty)
            .Observability(PropertyObservability.Changed)
            .Register();
    }

    public string String
    {
        get => GetValue(StringProperty);
        set => SetValue(StringProperty, value);
    }
}
