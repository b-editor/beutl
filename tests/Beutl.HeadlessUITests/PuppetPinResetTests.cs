using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using Point = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PuppetPinResetTests
{
    // A pin's Position defaults to the input's top-left corner. Reset in the pin row's menu puts the pin back on the
    // point of the picture it holds instead, as one edit, and on an animated pin into the keyframe the row edits.
    [AvaloniaTest]
    public async Task ResetPutsThePinBackOnItsRestPosition()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("puppet-pin-reset");
        var fixedRest = new Point(0.2f, 0.5f);
        var movingRest = new Point(0.7f, 0.4f);
        var fixedPin = new PuppetPin { RestPosition = { Value = fixedRest }, Position = { Value = fixedRest } };
        var moving = new PuppetPin { RestPosition = { Value = movingRest }, Position = { Value = movingRest } };
        var effect = new Puppet { Pins = { fixedPin, moving } };
        var shape = new RectShape { Width = { Value = 320 }, Height = { Value = 200 } };
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(effect);
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(4));
        Element element = scene.Children.Single();
        editor.GetRequiredService<IEditorSelection>().Select(element);

        using var inspector = new ElementPropertyTabViewModel(editor);
        inspector.Element.Value = element;
        var window = new Window { Width = 360, Height = 720, Content = new ElementPropertyTabView { DataContext = inspector } };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            FilterEffectEditorViewModel group = inspector.Items
                .SelectMany(item => Flatten(item.Properties, includeGroups: false))
                .OfType<FilterEffectEditorViewModel>()
                .Single(item => ReferenceEquals(item.Value.Value, shape.FilterEffect.Value));
            group.IsExpanded.Value = true;
            PuppetPinsEditor? pins = null;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render(2);
                FilterEffectListItemEditor? row = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                    .FirstOrDefault(item => item.DataContext is FilterEffectEditorViewModel vm && ReferenceEquals(vm.Value.Value, effect));
                if (row?.DataContext is not FilterEffectEditorViewModel effectEditor) return false;

                effectEditor.IsExpanded.Value = true;
                pins = row.GetVisualDescendants().OfType<PuppetPinsEditor>().FirstOrDefault();
                return Rows() is [{ IsEffectivelyVisible: true }, { IsEffectivelyVisible: true }];
            }, message: "The Puppet effect's two pin rows should be shown.");

            TextBox x = Rows()[1].GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_InnerFirstTextBox");
            int beforeMove = editor.HistoryManager.UndoCount;
            x.Focus();
            x.SelectAll();
            window.KeyTextInput(0.9f.ToString(CultureInfo.CurrentCulture));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount == beforeMove + 1,
                message: "Typing a coordinate should move the pin.");
            Point moved = moving.Position.Value;
            Assert.That(moved, Is.EqualTo(new Point(0.9f, movingRest.Y)));

            int beforeReset = editor.HistoryManager.UndoCount;
            ResetFromMenu(Rows()[1]);
            Assert.Multiple(() =>
            {
                Assert.That(moving.Position.Value, Is.EqualTo(movingRest));
                Assert.That(fixedPin.Position.Value, Is.EqualTo(fixedRest));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(beforeReset + 1));
            });
            editor.HistoryManager.Undo();
            Assert.That(moving.Position.Value, Is.EqualTo(moved));

            var start = new Point(0.5f, 0.5f);
            var end = new Point(0.9f, 0.1f);
            var animation = new KeyFrameAnimation<Point>
            {
                KeyFrames =
                {
                    new KeyFrame<Point> { Value = { Value = start } },
                    new KeyFrame<Point> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = end } }
                }
            };
            moving.Position.SetKeyFrameAnimation(animation);
            editor.HistoryManager.Commit("Animate test pin");
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(1));
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                Rows().ElementAtOrDefault(1)?.DataContext is PropertyEditorViewModel position
                && ReferenceEquals(position.EditingKeyFrame.Value, animation.KeyFrames[1]),
                message: "The row should edit the keyframe under the playhead.");

            int beforeKeyReset = editor.HistoryManager.UndoCount;
            ResetFromMenu(Rows()[1]);
            Assert.Multiple(() =>
            {
                Assert.That(moving.Position.GetKeyFrameAnimation(), Is.SameAs(animation));
                Assert.That(animation.KeyFrames.Select(key => key.Value), Is.EqualTo(new object[] { start, movingRest }));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(beforeKeyReset + 1));
            });
            editor.HistoryManager.Undo();
            Assert.That(animation.KeyFrames.Select(key => key.Value), Is.EqualTo(new object[] { start, end }));

            Vector2Editor<float>[] Rows() => pins?.GetVisualDescendants().OfType<Vector2Editor<float>>().ToArray() ?? [];
        }
        finally
        {
            window.Close();
        }

        static void ResetFromMenu(Vector2Editor<float> row)
        {
            var menu = (PropertyEditorMenu)row.MenuContent!;
            Button button = menu.GetVisualDescendants().OfType<Button>().Single();
            var actions = (FAMenuFlyout)button.ContextFlyout!;
            actions.ShowAt(button);
            HeadlessTestHelpers.Render(3);
            FAMenuFlyoutItem reset = actions.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "resetItem");
            Assert.That(reset.IsEffectivelyEnabled, Is.True);
            HeadlessTestHelpers.Click(reset);
            actions.Hide();
            HeadlessTestHelpers.Render(2);
        }
    }
}
