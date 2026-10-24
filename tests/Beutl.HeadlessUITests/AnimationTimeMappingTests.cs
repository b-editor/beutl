using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AnimationTimeMappingTests
{
    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(2)]
    public async Task Editors_seek_insert_and_place_keys_using_the_animations_owner(int origin)
    {
        await TestReset.ResetShellAsync();
        using var graph = await GraphScope.CreateAsync(elementStart: MediaTime.FromSeconds(5));
        var editor = (EditViewModel)graph.Model.EditorContext;
        Element element = graph.Model.Element!;
        var shape = element.Objects.OfType<RectShape>().Single();
        shape.IsTimeAnchor.Value = true;
        shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(origin), MediaTime.FromSeconds(10));
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        using var property = new NumberEditorViewModel<float>(adapter);
        property.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(Element), element), (typeof(Scene), editor.Scene), (typeof(IEditorClock), clock)));
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.AttachInline(adapter, element);
        InlineAnimationLayerViewModel inline = timeline.Inlines.Single(x => ReferenceEquals(x.Property, adapter));
        try
        {
            property.KeyFrameIndex.Value = 1;
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(origin + 1.5)));
            clock.Seek(MediaTime.FromSeconds(origin + 0.5));
            Assert.That(property.IsAtKeyFrame.Value, Is.True);
            Assert.That(property.KeyFrameIndex.Value, Is.Zero);
            Assert.That(graph.Model.ConvertKeyTime(clock.CurrentTime.Value), Is.EqualTo(graph.First.KeyTime.Value));
            Assert.That(inline.ConvertKeyTime(clock.CurrentTime.Value, graph.Animation), Is.EqualTo(graph.First.KeyTime.Value));
            Assert.That(graph.Model.Margin.Value.Left, Is.EqualTo(origin * 150));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(origin * 150));

            graph.Model.InsertKeyFrame(new Beutl.Animation.Easings.SplineEasing(), MediaTime.FromSeconds(origin + 1));
            Assert.That(graph.Animation.KeyFrames.Select(k => k.KeyTime.Value), Does.Contain(MediaTime.FromSeconds(1)));
            shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(3), MediaTime.FromSeconds(10));
            HeadlessTestHelpers.Settle();
            Assert.That(graph.Model.Margin.Value.Left, Is.EqualTo(450));
            Assert.That(inline.LeftMargin.Value.Left, Is.EqualTo(450));
            graph.Animation.UseGlobalClock.Value = true;
            Assert.That(graph.Model.Margin.Value.Left, Is.Zero);
            Assert.That(inline.LeftMargin.Value.Left, Is.Zero);
            property.KeyFrameIndex.Value = 2;
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
        }
        finally
        {
            timeline.DetachInline(inline);
        }
    }
}
