
using Beutl.Collections;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Beutl.Core.UnitTests;

public class PropertyChangeTrackerTests
{
    [SetUp]
    public void Setup()
    {
        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
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
        var array = new Hierarchical[] { elm };
        const string Foo = "Foo";
        const string Bar = "Bar";

        elm.String = Foo;
        using (var tracker = new PropertyChangeTracker(array, 0))
        {
            elm.String = Bar;
            elm_1.String = "Depth_1";

            IRecordableCommand command = tracker.ToCommand();

            command.Undo();

            Assert.That(elm.String, Is.EqualTo(Foo));
            Assert.That(elm_1.String, Is.EqualTo("Depth_1"));

            command.Redo();
            Assert.That(elm.String, Is.EqualTo(Bar));
            Assert.That(elm_1.String, Is.EqualTo("Depth_1"));
        }
    }
}

public class TestElement : Hierarchical, IHierarchical
{
    public static readonly CoreProperty<string> StringProperty;

    public TestElement()
    {
        Children = new HierarchicalList<Hierarchical>(this);
    }

    static TestElement()
    {
        StringProperty = ConfigureProperty<string, TestElement>("String")
            .DefaultValue(string.Empty)
            .Register();
    }

    public string String
    {
        get => GetValue(StringProperty);
        set => SetValue(StringProperty, value);
    }

    public HierarchicalList<Hierarchical> Children { get; set; }

    ICoreReadOnlyList<IHierarchical> IHierarchical.HierarchicalChildren => Children;
}
