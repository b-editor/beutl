using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// A key time read outside a project, as a template's is, falls up to a tick short of its frame; deleting, inserting and
// the keyframe mark still find that keyframe on its frame (#2885).
[TestFixture]
public sealed class TickShortKeyFrameLookupTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];

    [AvaloniaTest]
    public async Task Delete_insert_and_the_mark_find_a_keyframe_a_tick_short_of_its_frame([ValueSource(nameof(s_rates))] string rateText)
    {
        await TestReset.ResetShellAsync();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tick-short-keyframes");
        scene.FindHierarchicalParent<Project>()!.Variables[ProjectVariableKeys.FrameRate] = rateText;
        Rational rate = scene.FindHierarchicalParent<Project>().GetFrameRate();
        await TestShell.AddElementAsync(editor, () => new RectShape(), MediaTime.Zero, MediaTime.FromFrames(30, rate));
        Element element = scene.Children.First();
        var shape = (RectShape)element.Objects[0];
        var saved = new KeyFrameAnimation<float>();
        for (int frame = 0; frame < 3; frame++)
            saved.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromFrames(frame, rate), Value = 100 * (frame + 1) });
        var animation = (KeyFrameAnimation<float>)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(saved), typeof(KeyFrameAnimation<float>));
        shape.Width.SetKeyFrameAnimation(animation);
        Assert.That(animation.KeyFrames[1].KeyTime, Is.LessThan(MediaTime.FromFrames(1, rate)));

        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        HeadlessTestHelpers.Settle();
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        NumberEditorViewModel<float> width = PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<NumberEditorViewModel<float>>()
            .Single(x => ReferenceEquals(x.PropertyAdapter.GetEngineProperty(), shape.Width));

        ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(MediaTime.FromFrames(1, rate));
        HeadlessTestHelpers.Settle();
        bool marked = width.IsAtKeyFrame.Value;
        float index = width.KeyFrameIndex.Value;
        width.InsertKeyFrame(MediaTime.FromFrames(1, rate));
        int afterInsert = animation.KeyFrames.Count;
        width.RemoveKeyFrame(MediaTime.FromFrames(1, rate));

        Assert.Multiple(() =>
        {
            Assert.That(marked, Is.True);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(afterInsert, Is.EqualTo(3));
            Assert.That(animation.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 100f, 300f }));
        });
    }
}
