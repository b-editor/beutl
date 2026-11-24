using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PuppetEditingTests
{
    [AvaloniaTest]
    public async Task PreviewAddsAndDragsIndependentPinsAtTheRemappedTime()
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("puppet-pins", frameRate: 24);
        var effect = new Puppet();
        var initialPosition = new Point(0.35f, 0.45f);
        effect.Pins.Add(new PuppetPin { Position = { Value = initialPosition }, RestPosition = { Value = initialPosition } });
        var shape = new RectShape { Width = { Value = 320 }, Height = { Value = 210 } };
        ((FilterEffectGroup)shape.FilterEffect.Value!).Children.Add(effect);
        ((TransformGroup)shape.Transform.Value!).Children.Add(new RotationTransform(17));
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(4));
        Element element = scene.Children.Single();
        var remapped = MediaTime.FromSeconds(0.5);
        var timeController = new DrawableTimeController { OffsetPosition = { Value = remapped } };
        element.AddObject(timeController);
        editor.HistoryManager.Commit("Add test time controller");
        editor.GetRequiredService<IEditorSelection>().Select(element);

        using var inspector = new ElementPropertyTabViewModel(editor);
        inspector.Element.Value = element;
        var player = new PlayerView { DataContext = editor.Player };
        Grid.SetColumn(player, 1);
        var window = new Window
        {
            Width = 1180,
            Height = 630,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("280,*"),
                Children = { new ElementPropertyTabView { DataContext = inspector }, player }
            }
        };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render();
            editor.Player.IsMoveMode.Value = true;
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.Zero);
            await Render();
            FilterEffectEditorViewModel group = inspector.Items
                .SelectMany(item => Flatten(item.Properties, includeGroups: false))
                .OfType<FilterEffectEditorViewModel>()
                .Single(item => ReferenceEquals(item.Value.Value, shape.FilterEffect.Value));
            group.IsExpanded.Value = true;
            PuppetPinsEditor? pinsEditor = null;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render(2);
                FilterEffectListItemEditor? row = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                    .FirstOrDefault(item => item.DataContext is FilterEffectEditorViewModel vm && ReferenceEquals(vm.Value.Value, effect));
                if (row?.DataContext is not FilterEffectEditorViewModel effectEditor) return false;

                effectEditor.IsExpanded.Value = true;
                pinsEditor = row.GetVisualDescendants().OfType<PuppetPinsEditor>().FirstOrDefault();
                return pinsEditor is not null;
            });
            PuppetPinsEditor listControl = pinsEditor!;
            listControl.BringIntoView();
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render(2);
                return Rows() is [{ IsEffectivelyVisible: true }];
            }, message: "Existing pins should be visible as soon as the effect is opened.");
            TextBlock header = listControl.FindControl<TextBlock>("headerLabel")!;
            Button add = listControl.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == GraphicsStrings.Puppet_AddPin);
            Button remove = listControl.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == GraphicsStrings.Puppet_RemovePin);
            AvaPoint addCenter = HeadlessTestHelpers.Center(add, window);
            AvaPoint removeCenter = HeadlessTestHelpers.Center(remove, window);
            AvaPoint rightEdge = remove.TranslatePoint(new AvaPoint(remove.Bounds.Width, 0), listControl)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(header.Text, Is.EqualTo(GraphicsStrings.Puppet_Pins));
                Assert.That(listControl.GetVisualDescendants().OfType<ToggleButton>().Any(button => button.Name == "expandToggle"), Is.False);
                Assert.That(remove.IsEffectivelyVisible, Is.True);
                Assert.That(remove.IsEnabled, Is.False);
                Assert.That(removeCenter.X - addCenter.X, Is.EqualTo(add.Bounds.Width).Within(0.5));
                Assert.That(removeCenter.Y, Is.EqualTo(addCenter.Y).Within(0.5));
                Assert.That(rightEdge.X, Is.EqualTo(listControl.Bounds.Width - 8).Within(0.5));
            });
            HeadlessTestHelpers.Click(header);
            Assert.That(Rows().Single().IsEffectivelyVisible, Is.True);
            effect.Pins.RemoveAt(0);
            editor.HistoryManager.Commit("Remove initial test pin");
            Border placementHint = player.FindControl<Border>("puppetPlacementHint")!;
            editor.Player.FrameMatrix.Value = new Beutl.Graphics.Matrix(1.12f, 0, 0, 1.12f, -36, -25);
            await Render();

            int beforeAdd = editor.HistoryManager.UndoCount;
            await StartAdding(add);
            Assert.That(player.transformHandlesOverlay.HitTest(player.transformHandlesOverlay.PivotImage),
                Is.EqualTo(TransformHandlesOverlay.HandleKind.None));
            Capture("puppet-placement.png");
            ClickLocal(new Point(-20, 95));
            Assert.That(effect.Pins, Is.Empty);
            Assert.That(editor.Player.PointPick.Value, Is.Not.Null);
            ClickLocal(new Point(70, 100));
            await WaitForSelectedRow(1, 0);
            Assert.That(effect.Pins[0].Position.Value, Is.EqualTo(effect.Pins[0].RestPosition.Value));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(beforeAdd + 1));

            await WaitForPlacement();
            PreviewPointPick? beforeUndo = editor.Player.PointPick.Value;
            editor.HistoryManager.Undo();
            Assert.That(effect.Pins, Is.Empty);
            Assert.That(element.Objects.OfType<DrawableTimeController>().Single(), Is.SameAs(timeController));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PointPick.Value is { } pick
                && !ReferenceEquals(pick, beforeUndo));
            ClickLocal(new Point(70, 100));
            await WaitForSelectedRow(1, 0);
            Assert.That(editor.Player.PuppetPlacementEffect.Value, Is.SameAs(effect),
                "Undo refreshes the frame while keeping placement active");

            // The same placement session accepts the next pin.
            await WaitForPlacement();
            ClickLocal(new Point(251, 112));
            await WaitForSelectedRow(2, 1);
            PuppetPin fixedPin = effect.Pins[0];
            PuppetPin moving = effect.Pins[1];
            var coordinates = Rows()[1];
            Assert.Multiple(() =>
            {
                Assert.That(AutomationProperties.GetName(add), Is.EqualTo(GraphicsStrings.Puppet_FinishPlacement));
                Assert.That(placementHint.IsVisible, Is.True);
                Assert.That(editor.Player.SelectedPuppetPin.Value, Is.SameAs(moving));
                Assert.That(Rows().Select(row => GripNumber(row).Text), Is.EqualTo(new[] { "1", "2" }));
                Assert.That(Rows().Select(row => row.EditorStyle), Is.All.EqualTo(PropertyEditorStyle.ListItem));
                Assert.That(Rows().SelectMany(row => row.GetVisualDescendants().OfType<Button>())
                    .Any(button => button.Name == "PART_DeleteButton" && button.IsEffectivelyVisible), Is.False);
                Assert.That(coordinates.CornerRadius.TopLeft, Is.GreaterThan(0));
                Assert.That(GripNumber(coordinates).FontWeight, Is.EqualTo(Avalonia.Media.FontWeight.SemiBold));
                Assert.That(remove.IsEnabled, Is.True);
                Assert.That(NavigationButton(coordinates, "PART_LeftButton").IsEffectivelyVisible, Is.False);
                Assert.That(NavigationButton(coordinates, "PART_RightButton").IsEffectivelyVisible, Is.False);
                Assert.That(coordinates.MenuContent, Is.InstanceOf<PropertyEditorMenu>());
                Assert.That(coordinates.ReorderHandle, Is.Not.Null);
                Assert.That(coordinates.NumberFormat, Is.EqualTo("0.####"));
                Assert.That(coordinates.LargeChange, Is.EqualTo(0.01f));
                Assert.That(coordinates.SmallChange, Is.EqualTo(0.001f));
            });
            var xField = coordinates.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Name == "PART_InnerFirstTextBox");
            var yField = coordinates.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Name == "PART_InnerSecondTextBox");
            Assert.That(xField.TranslatePoint(default, coordinates)!.Value.Y,
                Is.EqualTo(yField.TranslatePoint(default, coordinates)!.Value.Y).Within(0.5));

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await HeadlessTestHelpers.WaitUntilAsync(() => PlacementEnded(add));
            Assert.That(effect.Pins, Has.Count.EqualTo(2));

            TextBlock firstNumber = GripNumber(Rows()[0]);
            Assert.That(firstNumber.IsVisible, Is.True);
            HeadlessTestHelpers.Click(firstNumber);
            await HeadlessTestHelpers.WaitUntilAsync(() => ReferenceEquals(editor.Player.SelectedPuppetPin.Value, fixedPin));
            Assert.That(RowIsSelected(0), Is.True);
            Point fixedPosition = fixedPin.Position.Value;
            var fixedX = Rows()[0].GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Name == "PART_InnerFirstTextBox");
            int beforeCoordinateEdit = editor.HistoryManager.UndoCount;
            fixedX.Focus();
            fixedX.SelectAll();
            window.KeyTextInput(0.25f.ToString(CultureInfo.CurrentCulture));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.UndoCount == beforeCoordinateEdit + 1,
                message: "Editing a coordinate commits one history entry when leaving the field.");
            Assert.That(fixedPin.Position.Value.X, Is.EqualTo(0.25f));
            editor.HistoryManager.Undo();
            Assert.That(fixedPin.Position.Value, Is.EqualTo(fixedPosition));
            await HeadlessTestHelpers.WaitUntilAsync(() => Rows().FirstOrDefault()?.FirstValue == fixedPosition.X);
            Point original = moving.Position.Value;
            var animation = new KeyFrameAnimation<Point>
            {
                KeyFrames =
                {
                    new KeyFrame<Point> { Value = { Value = original } },
                    new KeyFrame<Point> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = original } }
                }
            };
            moving.Position.SetKeyFrameAnimation(animation);
            editor.HistoryManager.Commit("Animate test pin");
            await Render();

            IEditorClock clock = editor.GetRequiredService<IEditorClock>();
            Button previousKey = NavigationButton(Rows()[1], "PART_LeftButton");
            Button nextKey = NavigationButton(Rows()[1], "PART_RightButton");
            Assert.Multiple(() =>
            {
                Assert.That(previousKey.IsEffectivelyVisible, Is.True);
                Assert.That(nextKey.IsEffectivelyVisible, Is.True);
                Assert.That(previousKey.IsEnabled, Is.False);
                Assert.That(nextKey.IsEnabled, Is.True);
            });
            HeadlessTestHelpers.Click(nextKey);
            await HeadlessTestHelpers.WaitUntilAsync(() => clock.CurrentTime.Value == MediaTime.FromSeconds(1));
            Assert.That(previousKey.IsEnabled, Is.True);
            Assert.That(nextKey.IsEnabled, Is.False);
            HeadlessTestHelpers.Click(previousKey);
            await HeadlessTestHelpers.WaitUntilAsync(() => clock.CurrentTime.Value == MediaTime.Zero);

            await StartAdding(add);
            Point from = effect.GetEditingHandles(MediaTime.Zero).Single(handle => handle.Id == moving.Id.Value.ToString()).Position;
            Point to = from + new Point(24, -54);
            int beforeDrag = editor.HistoryManager.UndoCount;
            // The hit target is kept in screen pixels. This press is outside the old zoomed marker radius.
            var grab = new Avalonia.Vector(10, 0);
            window.MouseDown(ToWindow(from) + grab, MouseButton.Left);
            window.MouseMove(ToWindow(to) + grab, RawInputModifiers.LeftMouseButton);
            window.MouseUp(ToWindow(to) + grab, MouseButton.Left);
            await Render();
            Assert.Multiple(() =>
            {
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(beforeDrag + 1));
                Assert.That(editor.Player.SelectedPuppetPin.Value, Is.SameAs(moving));
                Assert.That(RowIsSelected(1), Is.True);
                Assert.That(AutomationProperties.GetName(add), Is.EqualTo(GraphicsStrings.Puppet_AddPin));
                Assert.That(placementHint.IsVisible, Is.False);
                Assert.That(effect.Pins, Has.Count.EqualTo(2));
                Assert.That(animation.KeyFrames.Select(key => key.KeyTime.Value),
                    Is.EqualTo(new[] { MediaTime.Zero, remapped, MediaTime.FromSeconds(1) }));
                var written = (Point)animation.KeyFrames[1].Value!;
                Assert.That(written.X, Is.EqualTo(original.X + 24 / 320f).Within(0.01));
                Assert.That(written.Y, Is.EqualTo(original.Y - 54 / 210f).Within(0.01));
                Assert.That(fixedPin.Position.GetKeyFrameAnimation(), Is.Null);
                Assert.That(fixedPin.Position.Value, Is.EqualTo(fixedPin.RestPosition.Value));
            });

            if (Environment.GetEnvironmentVariable("BEUTL_PUPPET_CAPTURE") is { Length: > 0 })
            {
                Capture("puppet-editor.png");
                window.RequestedThemeVariant = ThemeVariant.Dark;
                HeadlessTestHelpers.Render();
                // Capture the final palette after the shared controls' brush transitions.
                await Task.Delay(Beutl.Controls.UiMotion.Normal);
                HeadlessTestHelpers.Render(3);
                Capture("puppet-editor-dark.png");
                window.RequestedThemeVariant = ThemeVariant.Light;
                HeadlessTestHelpers.Render(3);
            }

            byte[] preview = AlphaOf(editor.Player.PreviewImage.Value!.Value);
            Scene snapshot = SceneSnapshot.Create(scene);
            using (SceneRenderer renderer = ExportRendererFactory.Create(snapshot, TestShell.FontManager,
                       editor.Renderer.Value.OutputScale))
            using (var progress = new Subject<MediaTime>())
            using (var provider = new FrameProviderImpl(snapshot, new Rational(24), renderer, progress))
            using (Beutl.Media.Bitmap? exported = await provider.ReadNextFrame())
            {
                Assert.That(exported, Is.Not.Null);
                Assert.That(AlphaOf(exported!), Is.EqualTo(preview), "preview and the saved project's export frame");
            }

            editor.HistoryManager.Undo();
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            editor.HistoryManager.Redo();
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(3));

            Control secondRow = Rows()[1];
            Control grip = Rows()[0].ReorderHandle!;
            AvaPoint gripPoint = grip.TranslatePoint(new AvaPoint(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
            AvaPoint bottomOfRow = secondRow.TranslatePoint(new AvaPoint(0, secondRow.Bounds.Height - 2), window)!.Value;
            var dropPoint = new AvaPoint(gripPoint.X, bottomOfRow.Y);
            window.MouseDown(gripPoint, MouseButton.Left);
            window.MouseMove(dropPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(dropPoint, MouseButton.Left);
            Assert.That(effect.Pins[0], Is.SameAs(moving));
            await WaitForSelectedRow(2, 1);
            Assert.That(Rows().Select(row => GripNumber(row).Text), Is.EqualTo(new[] { "1", "2" }));
            editor.HistoryManager.Undo();
            Assert.That(effect.Pins[0], Is.SameAs(fixedPin));
            await WaitForSelectedRow(2, 0);

            remove.BringIntoView();
            HeadlessTestHelpers.Render(2);
            int beforeRemove = editor.HistoryManager.UndoCount;
            HeadlessTestHelpers.Click(remove);
            await WaitForSelectedRow(1, 0);
            Assert.That(effect.Pins.Single(), Is.SameAs(moving));
            Assert.That(editor.Player.SelectedPuppetPin.Value, Is.SameAs(moving));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(beforeRemove + 1));
            HeadlessTestHelpers.Click(remove);
            Assert.That(effect.Pins, Is.Empty);
            Assert.That(editor.Player.SelectedPuppetPin.Value, Is.Null);
            Assert.That(remove.IsEnabled, Is.False);
            editor.HistoryManager.Undo();
            editor.HistoryManager.Undo();
            Assert.That(effect.Pins.Select(pin => pin.Id.Value), Is.EqualTo(new[] { fixedPin.Id.Value, moving.Id.Value }));

            await StartAdding(add);
            AvaPoint rightClick = ToWindow(new Point(170, 92));
            window.MouseDown(rightClick, MouseButton.Right);
            window.MouseUp(rightClick, MouseButton.Right);
            await HeadlessTestHelpers.WaitUntilAsync(() => PlacementEnded(add));

            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(5));
            await Render();
            add.BringIntoView();
            HeadlessTestHelpers.Render(2);
            HeadlessTestHelpers.Click(add);
            await HeadlessTestHelpers.WaitUntilAsync(() => PlacementEnded(add));
            Assert.That(effect.Pins, Has.Count.EqualTo(2));

            Vector2Editor<float>[] Rows() => listControl.GetVisualDescendants().OfType<Vector2Editor<float>>().ToArray();

            static TextBlock GripNumber(Vector2Editor<float> row) => (TextBlock)((Border)row.ReorderHandle!).Child!;

            static Button NavigationButton(Vector2Editor<float> row, string name) => row.GetVisualDescendants()
                .OfType<Button>().Single(button => button.Name == name);

            bool RowIsSelected(int index) => Rows().ElementAtOrDefault(index)?.Classes.Contains("selected") == true;

            async Task WaitForSelectedRow(int count, int index)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                {
                    HeadlessTestHelpers.Render(2);
                    var rows = Rows();
                    return effect.Pins.Count == count && rows.Length == count && rows[index].Classes.Contains("selected");
                }, message: $"Expected {count} pin rows with row {index + 1} selected.");
            }

            bool PlacementEnded(Button button) => AutomationProperties.GetName(button) == GraphicsStrings.Puppet_AddPin
                && editor.Player.PuppetPlacementEffect.Value is null && !placementHint.IsVisible;
        }
        finally
        {
            window.Close();
        }

        async Task StartAdding(Button add)
        {
            add.BringIntoView();
            HeadlessTestHelpers.Render(2);
            HeadlessTestHelpers.Click(add, move: true);
            await WaitForPlacement();
        }

        async Task WaitForPlacement()
        {
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PointPick.Value is not null,
                message: "Puppet did not open the preview point picker.");
            await Render();
            Assert.That(player.transformHandlesOverlay.Drawable, Is.SameAs(shape),
                $"The selected Puppet's drawable could not be resolved. Preview viewport present: {player.ViewportTransform is not null}.");
        }

        void Capture(string fileName)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_PUPPET_CAPTURE") is not { Length: > 0 } capture) return;

            Directory.CreateDirectory(capture);
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            frame.Save(Path.Combine(capture, fileName), PngBitmapEncoderOptions.Default);
        }

        async Task Render()
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
            editor.Player.QueuePreviewRender();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(3);
        }

        void ClickLocal(Point point)
        {
            AvaPoint position = ToWindow(point);
            window.MouseDown(position, MouseButton.Left);
            window.MouseUp(position, MouseButton.Left);
        }

        AvaPoint ToWindow(Point local)
        {
            Point frame = player.transformHandlesOverlay.UserMatrix.Transform(local);
            AvaPoint point = player.transformHandlesOverlay.Viewport!.ToPreview(new AvaPoint(frame.X, frame.Y));
            return player.image.TranslatePoint(point, window)!.Value;
        }
    }

    private static byte[] AlphaOf(Beutl.Media.Bitmap frame)
    {
        using Beutl.Media.Bitmap alpha = frame.Convert(BitmapColorType.Alpha8);
        return alpha.GetPixelSpan().ToArray();
    }
}
