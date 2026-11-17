using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Moq;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

// An effect with editing handles has a crosshair left of its eye, which selects the effect so that the preview shows its
// handles.
[TestFixture]
[NonParallelizable]
public sealed class FilterEffectSelectButtonTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_crosshair_left_of_the_eye_selects_an_item_with_editing_handles(bool light)
    {
        var blur = new Blur();
        var handles = new EditingHandleTestEffect();
        var element = new Element
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"select-effect-{Guid.NewGuid():N}.belm")),
            Objects = { new RectShape { FilterEffect = { Value = new FilterEffectGroup { Children = { blur, handles } } } } }
        };
        var selection = new Selection();
        using var history = new HistoryManager(new OperationSequenceGenerator());
        using var tab = new ElementPropertyTabViewModel(Host(selection, history));
        var window = new Window
        {
            Content = new ElementPropertyTabView { DataContext = tab },
            Width = 360,
            Height = 520,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            tab.Element.Value = element;
            window.Show();
            Flatten(tab.Items.Single().Properties, includeGroups: false)
                .OfType<FilterEffectEditorViewModel>().Single().IsExpanded.Value = true;
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);

            FilterEffectListItemEditor[] rows = [.. window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()];
            FilterEffectListItemEditor row = rows.Single(row => Effect(row) == handles);
            row.BringIntoView();
            HeadlessTestHelpers.Render(3);
            Button button = row.FindControl<Button>("selectEffectButton")!;
            Assert.That(rows.Single(row => Effect(row) == blur).FindControl<Button>("selectEffectButton")!.IsVisible,
                Is.False, "an effect without handles has nothing for the preview to show");
            AssertLeftOfTheEye(button, Eye(row), window);

            Click(button, window);
            Assert.That(selection.SelectedObject.Value, Is.SameAs(handles));
            Capture(window, $"item-{(light ? "light" : "dark")}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void The_crosshair_of_a_single_effect_sits_left_of_the_eye_in_the_header()
    {
        var handles = new EditingHandleTestEffect();
        var shape = new RectShape { FilterEffect = { Value = handles } };
        using var history = new HistoryScope(shape);
        var selection = new Selection();
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(new ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
            (typeof(ExtensionProvider), TestShell.Extensions),
            (typeof(IEditorSelection), selection)));
        var editor = new FilterEffectEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 360, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Button button = editor.FindControl<Button>("selectEffectButton")!;
            AssertLeftOfTheEye(button, Eye(editor), window);

            Click(button, window);
            Assert.That(selection.SelectedObject.Value, Is.SameAs(handles));
            Capture(window, "header");
        }
        finally { window.Close(); }
    }

    // What the crosshair is for: the preview then shows the effect's handle, and a drag on it edits the effect in one undo
    // step, whether the effect moves the handle itself or the handle writes a property.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_handle_of_the_effect_the_crosshair_selected_is_dragged_in_the_preview(bool bound)
    {
        TestGpu.RequireAnyDevice();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("select-effect-drag");
        var effect = new EditingHandleTestEffect { Bound = bound };
        await TestShell.AddElementAsync(
            editor,
            () => new RectShape
            {
                Width = { Value = 180 },
                Height = { Value = 120 },
                FilterEffect = { Value = new FilterEffectGroup { Children = { effect } } }
            },
            MediaTime.Zero, MediaTime.FromSeconds(4));
        Element element = scene.Children.Single();
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.Select(element);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        var player = new PlayerView { DataContext = editor.Player };
        Grid.SetColumn(player, 1);
        var window = new Window
        {
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("360,*"),
                Children = { new ElementPropertyTabView { DataContext = tab }, player }
            },
            Width = 1260,
            Height = 560
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            editor.Player.IsMoveMode.Value = true;
            await RenderPreview(editor);
            Flatten(tab.Items.Single().Properties, includeGroups: false)
                .OfType<FilterEffectEditorViewModel>().Single().IsExpanded.Value = true;
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);
            FilterEffectListItemEditor row = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                .Single(row => Effect(row) == effect);
            row.BringIntoView();
            HeadlessTestHelpers.Render(3);

            Click(row.FindControl<Button>("selectEffectButton")!, window);
            Assert.That(selection.SelectedObject.Value, Is.SameAs(effect));
            await RenderPreview(editor);

            TransformHandlesOverlay overlay = player.transformHandlesOverlay;
            var viewport = overlay.Viewport!;
            BtlPoint before = effect.Position.Value;
            BtlPoint frame = overlay.UserMatrix.Transform(before);
            Point from = viewport.ToPreview(new Point(frame.X, frame.Y));
            Point to = from + new Vector(32, 19);
            Assert.That(overlay.HitTestEditingHandle(from)?.Provider, Is.SameAs(effect), "the preview shows the handle");
            Point targetFrame = viewport.ToFrame(to);
            BtlPoint expected = overlay.UserMatrix.Invert().Transform(new BtlPoint((float)targetFrame.X, (float)targetFrame.Y));
            int undoCount = editor.HistoryManager.UndoCount;

            window.MouseDown(player.image.TranslatePoint(from, window)!.Value, MouseButton.Left);
            window.MouseMove(player.image.TranslatePoint(to, window)!.Value, RawInputModifiers.LeftMouseButton);
            window.MouseUp(player.image.TranslatePoint(to, window)!.Value, MouseButton.Left);
            Assert.Multiple(() =>
            {
                Assert.That(effect.Position.Value.X, Is.EqualTo(expected.X).Within(0.01));
                Assert.That(effect.Position.Value.Y, Is.EqualTo(expected.Y).Within(0.01));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
            });

            editor.HistoryManager.Undo();
            Assert.That(effect.Position.Value, Is.EqualTo(before));
        }
        finally { window.Close(); }
    }

    // Without a selection there is nothing to select the effect in.
    [AvaloniaTest]
    public void No_crosshair_shows_where_nothing_can_be_selected()
    {
        var handles = new EditingHandleTestEffect();
        var shape = new RectShape { FilterEffect = { Value = handles } };
        using var history = new HistoryScope(shape);
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));

        Assert.That(viewModel.CanSelectEffect.Value, Is.False);
    }

    // The tab shows several elements through a copy of one, which is in no scene: selecting an effect of the copy would
    // select nothing the preview shows.
    [AvaloniaTest]
    public async Task The_copy_that_stands_for_several_elements_offers_no_crosshair()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("select-effect-several");
        for (int i = 0; i < 2; i++)
        {
            await TestShell.AddElementAsync(
                editor,
                () => new RectShape { FilterEffect = { Value = new FilterEffectGroup { Children = { new EditingHandleTestEffect() } } } },
                MediaTime.FromSeconds(i), MediaTime.FromSeconds(1), i);
        }

        Element[] elements = [.. scene.Children.OrderBy(element => element.Start.Value)];
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.Select(elements[0]);
        selection.SelectedElements.Add(elements[1]);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = elements[0];
        HeadlessTestHelpers.Settle();
        Assert.That(tab.MultipleElements.Value, Is.Not.Null);

        FilterEffectEditorViewModel copy = Item(tab);
        Assert.Multiple(() =>
        {
            Assert.That(copy.EditsSeveralElements, Is.True);
            Assert.That(copy.CanSelectEffect.Value, Is.False);
        });

        selection.SelectedElements.Remove(elements[1]);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.MultipleElements.Value, Is.Null);

        FilterEffectEditorViewModel own = Item(tab);
        Assert.That(own.CanSelectEffect.Value, Is.True);
        own.SelectEffect();
        Assert.That(selection.SelectedObject.Value, Is.SameAs(
            ((FilterEffectGroup)((RectShape)elements[0].Objects[0]).FilterEffect.Value!).Children[0]));

        // The editor of the only effect of the shown element's shape.
        static FilterEffectEditorViewModel Item(ElementPropertyTabViewModel tab)
        {
            FilterEffectEditorViewModel filters = Flatten(tab.Items.Single().Properties, includeGroups: false)
                .OfType<FilterEffectEditorViewModel>().Single();
            filters.IsExpanded.Value = true;
            HeadlessTestHelpers.Settle();
            var item = (FilterEffectEditorViewModel)filters.Group.Value!.Items.Single().Context!;
            Assert.That(item.Value.Value, Is.TypeOf<EditingHandleTestEffect>());
            return item;
        }
    }

    private static FilterEffect? Effect(FilterEffectListItemEditor row) => ((FilterEffectEditorViewModel)row.DataContext!).Value.Value;

    // The visibility toggle shown in the editor.
    private static ToggleButton Eye(Control editor)
    {
        object? theme = Application.Current!.FindResource("VisibilityToggleButtonStyle");
        return editor.GetVisualDescendants().OfType<ToggleButton>()
            .Single(toggle => toggle.IsEffectivelyVisible && ReferenceEquals(toggle.Theme, theme));
    }

    private static void AssertLeftOfTheEye(Button button, ToggleButton eye, Window window)
    {
        Rect buttonBounds = Bounds(button, window);
        Rect eyeBounds = Bounds(eye, window);
        Assert.Multiple(() =>
        {
            Assert.That(button.IsEffectivelyVisible, Is.True);
            Assert.That(buttonBounds.Size, Is.EqualTo(eyeBounds.Size), "as big as the eye");
            Assert.That(buttonBounds.Right + 4, Is.EqualTo(eyeBounds.Left).Within(0.5), "4 px left of the eye");
            Assert.That(buttonBounds.Center.Y, Is.EqualTo(eyeBounds.Center.Y).Within(0.5));
        });
    }

    private static Rect Bounds(Control control, Visual relativeTo)
        => new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    private static async Task RenderPreview(EditViewModel editor)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.AfterRendered.Take(1).Subscribe(_ => rendered.TrySetResult());
        editor.Player.QueuePreviewRender();
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        HeadlessTestHelpers.Render();
    }

    private static void Click(Button button, Window window)
    {
        Point center = HeadlessTestHelpers.Center(button, window);
        window.MouseMove(center);
        TimelineTestHost.WaitForHitTest(window, button, center);
        HeadlessTestHelpers.Click(button);
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_SELECT_EFFECT_CAPTURE") is not { Length: > 0 } directory) return;

        Directory.CreateDirectory(directory);
        window.SetRenderScaling(2);
        HeadlessTestHelpers.Render(3);
        using WriteableBitmap? image = window.CaptureRenderedFrame();
        Assert.That(image, Is.Not.Null);
        image!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }

    private static IEditorContext Host(IEditorSelection selection, HistoryManager history)
    {
        ExtensionProvider extensions = TestShell.Extensions;
        var factory = new PropertyEditorFactoryAdapter(extensions);
        var host = new Mock<IEditorContext>();
        host.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
        host.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        host.Setup(h => h.GetService(It.IsAny<Type>())).Returns((Type type) =>
            type == typeof(IEditorSelection) ? selection :
            type == typeof(IPropertyEditorFactory) ? factory :
            type == typeof(ExtensionProvider) ? extensions :
            type == typeof(HistoryManager) ? history : null);
        return host.Object;
    }

    private sealed class Selection : IEditorSelection
    {
        private readonly ReactivePropertySlim<CoreObject?> _selected = new();

        public IReadOnlyReactiveProperty<CoreObject?> SelectedObject => _selected;

        public IReadOnlyReactiveProperty<int?> SelectedLayerNumber { get; } = new ReactivePropertySlim<int?>();

        public ICoreList<Element> SelectedElements { get; } = new CoreList<Element>();

        public void Select(CoreObject? obj, bool notifyIfSelected = false) => _selected.Value = obj;
    }
}
