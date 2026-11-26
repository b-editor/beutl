using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
public sealed class TextOnPathEditingTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EditInTabMovesTheTextAndUndoRestoresIt(bool useDefaultPath)
    {
        TestGpu.RequireAnyDevice();
        var (_, scene, editor) = await TestShell.OpenNewSceneAsync("text-on-path", width: 520, height: 300);
        var effect = new TextOnPathEffect();
        if (!useDefaultPath)
            effect.Path.Value = PathGeometry.Parse("M42 218 C150 48 304 44 447 170");
        var path = (PathGeometry)effect.Path.Value!;
        path.Transform.Value = new TranslateTransform(12, 8);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = 78 },
            Easing = { Value = new LinearEasing() },
        });
        effect.PathOffset.SetKeyFrameAnimation(animation);
        var text = new TextBlock
        {
            Text = { Value = "Text along a path" },
            Size = { Value = 48 },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Transform = { Value = new TranslateTransform(9, 6) },
            SplitByCharacters = { Value = true },
        };
        ((FilterEffectGroup)text.FilterEffect.Value!).Children.Add(effect);
        await TestShell.AddElementAsync(editor, () => text, MediaTime.Zero, MediaTime.FromSeconds(4));

        using var inspector = new ElementPropertyTabViewModel(editor);
        inspector.Element.Value = scene.Children.Single();
        var group = PropertyEditorTestHost.Flatten(inspector.Items.Single().Properties, includeGroups: false)
            .OfType<FilterEffectEditorViewModel>().Single();
        group.IsExpanded.Value = true;
        var effectEditor = (FilterEffectEditorViewModel)group.Group.Value!.Items.Single().Context!;
        effectEditor.IsExpanded.Value = true;
        var geometryEditor = effectEditor.Properties.Value!.Properties.OfType<GeometryEditorViewModel>().Single();
        geometryEditor.ExpandForEditing();

        var player = new PlayerView { DataContext = editor.Player };
        var tabHost = new ContentControl();
        Grid.SetRow(tabHost, 1);
        var preview = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            Children = { player, tabHost },
        };
        Grid.SetColumn(preview, 1);
        var window = new Window
        {
            Width = 1080,
            Height = 730,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("330,*"),
                Children = { new ElementPropertyTabView { DataContext = inspector }, preview },
            },
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            PathFigureListItemEditor? row = null;
            PathFigure figure = path.Figures.Single();
            await HeadlessTestHelpers.WaitUntilAsync(() => (row = window.GetVisualDescendants().OfType<PathFigureListItemEditor>()
                .SingleOrDefault(item => item.DataContext is PathFigureEditorViewModel vm && ReferenceEquals(vm.Value.Value, figure))) is not null);
            var menu = row!.GetVisualDescendants().OfType<ToggleButton>().Select(button => button.ContextFlyout)
                .OfType<FAMenuFlyout>().Single();
            ClickMenuItem(menu, Strings.EditInFrame);
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.Player.PathEditor.GeometryResource.Value is not null
                && editor.Player.PathEditor.AvaMatrix.Value != Matrix.Identity);
            Assert.That(editor.Player.PathEditor.AvaMatrix.Value.Transform(default(Point)), Is.EqualTo(new Point(21, 14)));
            ClickMenuItem(menu, Strings.EditInFrame);

            ClickMenuItem(menu, Strings.EditInTab);
            var model = editor.FindToolTab<PathEditorTabViewModel>()!;
            Assert.That(model.PathGeometry.Value, Is.SameAs(path));
            var tab = new PathEditorTabView { DataContext = model };
            tabHost.Content = tab;
            if (HeadlessCapture.IsRequested)
            {
                window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                    .Single(item => ReferenceEquals(item.DataContext, effectEditor)).BringIntoView();
            }
            byte[] initial = await Render(editor);
            HeadlessCapture.SaveIfRequested(window, nameof(TextOnPathEditingTests),
                useDefaultPath ? "default-path-and-editor" : "curve-and-path-editor");

            PathSegment segment = figure.Segments.Single();
            var endPoint = segment.GetEndPoint();
            var originalEnd = endPoint.Value;
            Thumb anchor = PathEditorInteractionTests.Fixture.FindAnchor(tab, segment);
            Point start = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), window)!.Value;
            Point end = start + new Vector(-36, 50);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render();
            window.MouseUp(end, MouseButton.Left);

            Assert.That(endPoint.Value, Is.Not.EqualTo(originalEnd));
            Assert.That(await Render(editor), Is.Not.EqualTo(initial));
            Assert.That(editor.HistoryManager.Undo(), Is.True);
            Assert.That(endPoint.Value, Is.EqualTo(originalEnd));
            Assert.That(await Render(editor), Is.EqualTo(initial));

            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(1));
            Assert.That(await Render(editor), Is.Not.EqualTo(initial));
            HeadlessCapture.SaveIfRequested(window, nameof(TextOnPathEditingTests),
                useDefaultPath ? "default-path-offset" : "animated-offset");
        }
        finally
        {
            window.Close();
        }
    }

    private static void ClickMenuItem(FAMenuFlyout menu, string text)
        => menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == text)
            .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));

    private static async Task<byte[]> Render(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render(3);
        using var frame = editor.Player.PreviewImage.Value?.TryClone();
        Assert.That(frame, Is.Not.Null);
        using Bitmap alpha = frame!.Value.Convert(BitmapColorType.Alpha8);
        byte[] pixels = alpha.GetPixelSpan().ToArray();
        Assert.That(pixels, Has.Some.Not.Zero);
        return pixels;
    }
}
