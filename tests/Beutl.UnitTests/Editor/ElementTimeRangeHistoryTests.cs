using Beutl.Editor.Operations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class ElementTimeRangeHistoryTests
{
    [Test]
    public void ElementEdit_RecordsOnlyTheElement_AndUndoRestoresTheRangesAndLayersOfItsObjects()
    {
        using var harness = new SceneHistoryHarness("beutl_element_time_range");
        var app = new BeutlApplication { Project = new Project() };
        app.Project!.Items.Add(harness.Scene);
        Element element = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), zIndex: 1);
        var inner = new RectShape();
        var group = new DrawableGroup();
        group.Children.Add(inner);
        element.Objects.Add(group);
        harness.History.Commit("Add");
        var before = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.That(inner.TimeRange, Is.EqualTo(before));

        element.Start = TimeSpan.FromSeconds(3);
        element.ZIndex = 2;
        harness.History.Commit("Move");

        Assert.That(harness.History.PeekUndo()!.Operations.Select(o => (o as IUpdatePropertyValueOperation)?.Object),
            Is.EqualTo(new[] { element, element }));
        Assert.That(inner.TimeRange, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2))));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            foreach (EngineObject obj in new EngineObject[] { group, inner })
            {
                Assert.That(obj.TimeRange, Is.EqualTo(before));
                Assert.That(obj.ZIndex, Is.EqualTo(1));
            }
        });
    }
}
