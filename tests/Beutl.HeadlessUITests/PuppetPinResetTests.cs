using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    }

    // With a pin's Position separated into X and Y, Reset in the menu of a component's row puts that component back on
    // the rest position's and leaves the other where it is, each as one edit.
    [AvaloniaTest]
    public async Task ResetPutsASeparatedComponentBackOnTheRestPosition()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("puppet-pin-reset-components");
        var rest = new Point(0.7f, 0.4f);
        var pin = new PuppetPin { RestPosition = { Value = rest }, Position = { Value = rest } };
        await TestShell.AddElementAsync(editor, () => PuppetShape(pin), MediaTime.Zero, MediaTime.FromSeconds(4));
        Element element = scene.Children.Single();
        editor.GetRequiredService<IEditorSelection>().Select(element);

        using var inspector = new ElementPropertyTabViewModel(editor);
        inspector.Element.Value = element;
        var window = new Window { Width = 360, Height = 720, Content = new ElementPropertyTabView { DataContext = inspector } };
        window.Show();
        try
        {
            Vector2Editor<float> row = (await ShowPinRowsAsync(window, inspector, count: 1)).Single();
            ((PropertyEditorViewModel)row.DataContext!).SeparateComponents();
            IReadOnlyList<IProperty> components = ((IComponentAnimation)pin.Position.GetPropertyAnimation()!).Components;
            var moved = new Point(0.9f, 0.1f);
            components.Single(component => component.Name == "X").Value = moved.X;
            components.Single(component => component.Name == "Y").Value = moved.Y;
            editor.HistoryManager.Commit("Move test pin");

            NumberEditor<float>[] rows = await ShowComponentRowsAsync(window, row);
            Assert.That(rows.Select(component => component.Header), Is.EqualTo(new[] { "X", "Y" }));

            int before = editor.HistoryManager.UndoCount;
            ResetFromMenu(rows[0]);
            Assert.Multiple(() =>
            {
                Assert.That(SeparatedPosition(pin), Is.EqualTo(new Point(rest.X, moved.Y)));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            });

            ResetFromMenu(rows[1]);
            Assert.Multiple(() =>
            {
                Assert.That(SeparatedPosition(pin), Is.EqualTo(rest));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 2));
            });

            editor.HistoryManager.Undo();
            Assert.That(SeparatedPosition(pin), Is.EqualTo(new Point(rest.X, moved.Y)));
            editor.HistoryManager.Undo();
            Assert.That(SeparatedPosition(pin), Is.EqualTo(moved));
        }
        finally
        {
            foreach (var popup in window.OpenedPopups.ToArray())
                popup.Close();
            window.Close();
        }
    }

    // With several elements edited together, Reset in the menu of the pin row the tab shows puts each element's pin back
    // on its own rest position, not on the one of the pin shown, and one undo brings them all back.
    [AvaloniaTest]
    public async Task ResetPutsThePinOfEachElementBackOnItsOwnRestPosition()
    {
        Point[] rests = [new(0.2f, 0.5f), new(0.6f, 0.3f)];
        Point[] moved = [new(0.9f, 0.1f), new(0.1f, 0.8f)];
        PuppetPin[] pins = Pins(rests, moved);
        (EditViewModel editor, ElementPropertyTabViewModel inspector) = await OpenTogetherAsync("puppet-pin-reset-elements", pins);
        var window = new Window { Width = 360, Height = 720, Content = new ElementPropertyTabView { DataContext = inspector } };
        window.Show();
        try
        {
            Vector2Editor<float> row = (await ShowPinRowsAsync(window, inspector, count: 1)).Single();
            int before = editor.HistoryManager.UndoCount;
            ResetFromMenu(row);
            Assert.Multiple(() =>
            {
                Assert.That(pins.Select(pin => pin.Position.Value), Is.EqualTo(rests));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            });

            editor.HistoryManager.Undo();
            Assert.That(pins.Select(pin => pin.Position.Value), Is.EqualTo(moved));
        }
        finally
        {
            window.Close();
            inspector.Dispose();
        }
    }

    // With several elements edited together and the Position of each pin separated, Reset in the menu of a component's
    // row puts that component of each element's pin back on its own rest position's, as one edit.
    [AvaloniaTest]
    public async Task ResetPutsASeparatedComponentOfEachElementBackOnItsOwnRestPosition()
    {
        Point[] rests = [new(0.2f, 0.5f), new(0.6f, 0.3f)];
        Point[] moved = [new(0.9f, 0.1f), new(0.1f, 0.8f)];
        PuppetPin[] pins = Pins(rests, moved);
        foreach (PuppetPin pin in pins)
            ComponentAnimations.Separate(pin.Position);
        (EditViewModel editor, ElementPropertyTabViewModel inspector) =
            await OpenTogetherAsync("puppet-pin-reset-element-components", pins);
        var window = new Window { Width = 360, Height = 720, Content = new ElementPropertyTabView { DataContext = inspector } };
        window.Show();
        try
        {
            Vector2Editor<float> row = (await ShowPinRowsAsync(window, inspector, count: 1)).Single();
            NumberEditor<float>[] rows = await ShowComponentRowsAsync(window, row);
            int before = editor.HistoryManager.UndoCount;
            ResetFromMenu(rows[0]);
            Assert.Multiple(() =>
            {
                Assert.That(pins.Select(SeparatedPosition),
                    Is.EqualTo(rests.Zip(moved, (rest, position) => new Point(rest.X, position.Y))));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            });

            editor.HistoryManager.Undo();
            Assert.That(pins.Select(SeparatedPosition), Is.EqualTo(moved));
        }
        finally
        {
            foreach (var popup in window.OpenedPopups.ToArray())
                popup.Close();
            window.Close();
            inspector.Dispose();
        }
    }

    // A pin resting at each of rests, moved to the position at its place in positions.
    private static PuppetPin[] Pins(Point[] rests, Point[] positions)
        => [.. rests.Zip(positions, (rest, position) => new PuppetPin { RestPosition = { Value = rest }, Position = { Value = position } })];

    // Opens a scene with an element for each pin, which a Puppet on a shape holds, and a tab that edits them together.
    private static async Task<(EditViewModel Editor, ElementPropertyTabViewModel Inspector)> OpenTogetherAsync(
        string name, PuppetPin[] pins)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
        for (int i = 0; i < pins.Length; i++)
        {
            PuppetPin pin = pins[i];
            await TestShell.AddElementAsync(editor, () => PuppetShape(pin), MediaTime.Zero, MediaTime.FromSeconds(4), i);
        }

        Element[] elements = [.. scene.Children.OrderBy(element => element.ZIndex.Value)];
        var selection = editor.GetRequiredService<IEditorSelection>();
        selection.Select(elements[0]);
        foreach (Element element in elements.Skip(1))
            selection.SelectedElements.Add(element);

        var inspector = new ElementPropertyTabViewModel(editor);
        inspector.Element.Value = elements[0];
        HeadlessTestHelpers.Settle();
        Assert.That(inspector.MultipleElements.Value?.Elements, Is.EqualTo(elements));
        return (editor, inspector);
    }

    // The point that the separated X and Y of a pin's Position hold.
    private static Point SeparatedPosition(PuppetPin pin)
    {
        IReadOnlyList<IProperty> components = ((IComponentAnimation)pin.Position.GetPropertyAnimation()!).Components;
        return new Point(
            (float)components.Single(component => component.Name == "X").Value!,
            (float)components.Single(component => component.Name == "Y").Value!);
    }

    // A shape whose Puppet effect holds pin.
    private static RectShape PuppetShape(PuppetPin pin)
    {
        var shape = new RectShape { Width = { Value = 320 }, Height = { Value = 200 } };
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(new Puppet { Pins = { pin } });
        return shape;
    }

    // Shows the pin rows of the Puppet on the shape the tab shows, which stands for every element it edits.
    private static async Task<Vector2Editor<float>[]> ShowPinRowsAsync(
        Window window, ElementPropertyTabViewModel inspector, int count)
    {
        var shape = (RectShape)inspector.DisplayedElement.Value!.Objects[0];
        var effects = (FilterEffectGroup)shape.FilterEffect.Value!;
        Puppet effect = effects.Children.OfType<Puppet>().Single();
        HeadlessTestHelpers.Render();
        FilterEffectEditorViewModel group = inspector.Items
            .SelectMany(item => Flatten(item.Properties, includeGroups: false))
            .OfType<FilterEffectEditorViewModel>()
            .Single(item => ReferenceEquals(item.Value.Value, effects));
        group.IsExpanded.Value = true;
        Vector2Editor<float>[] rows = [];
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render(2);
            FilterEffectListItemEditor? item = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                .FirstOrDefault(editor => editor.DataContext is FilterEffectEditorViewModel vm && ReferenceEquals(vm.Value.Value, effect));
            if (item?.DataContext is not FilterEffectEditorViewModel effectEditor) return false;

            effectEditor.IsExpanded.Value = true;
            rows = [.. item.GetVisualDescendants().OfType<PuppetPinsEditor>()
                .SelectMany(pins => pins.GetVisualDescendants().OfType<Vector2Editor<float>>())];
            return rows.Length == count && rows.All(row => row.IsEffectivelyVisible);
        }, message: $"The Puppet effect's {count} pin rows should be shown.");
        return rows;
    }

    // Opens the flyout that a separated row's menu button shows, and gives the row of each of its components.
    private static async Task<NumberEditor<float>[]> ShowComponentRowsAsync(Window window, PropertyEditor row)
    {
        Button button = ((PropertyEditorMenu)row.MenuContent!).GetVisualDescendants().OfType<Button>().Single();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        NumberEditor<float>[] rows = [];
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render(2);
            rows = [.. window.OpenedPopups.Select(popup => popup.Child).OfType<FlyoutPresenter>()
                .Select(presenter => presenter.Content).OfType<ComponentEditors>()
                .SelectMany(editors => editors.GetVisualDescendants().OfType<NumberEditor<float>>())
                .Where(component => component.IsEffectivelyVisible)];
            return rows.Length == 2;
        }, message: "The flyout should show a row for each component of the pin's position.");
        return rows;
    }

    private static void ResetFromMenu(PropertyEditor row)
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
