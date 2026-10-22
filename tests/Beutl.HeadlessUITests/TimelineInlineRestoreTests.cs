using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineInlineRestoreTests
{
    // The saved state names the keyframes by Id; finding them needs the search to reach each IProperty.
    [AvaloniaTest]
    public async Task Inline_animation_layers_come_back_from_the_saved_state()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("inline-restore");
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.Zero, Value = 100, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromSeconds(1), Value = 200, Easing = new LinearEasing() });
        shape.Width.SetKeyFrameAnimation(animation);
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        Element element = scene.Children.Single();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.AttachInline(new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape), element);
        var json = new JsonObject();
        timeline.WriteToJson(json);
        timeline.DetachInline(timeline.Inlines.Single());
        Assert.That(timeline.Inlines, Is.Empty);

        timeline.ReadFromJson(json);

        Assert.That(timeline.Inlines.Single().Property.Animation, Is.SameAs(animation));
    }
}
