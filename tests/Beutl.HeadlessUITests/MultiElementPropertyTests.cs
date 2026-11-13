using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

// The element property tab edits the selected elements together: it shows a copy of one of them in its usual view, of
// which it shows only what every element holds, and each edit of the copy happens to every element as its own editor
// would make it.
[TestFixture]
public class MultiElementPropertyTests
{
    [AvaloniaTest]
    public async Task ValueEditsReachEveryElementInOneUndoStep()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        int before = editor.HistoryManager.UndoCount;

        ((IValueEditorContext<float>)opacity).PreviewEdit(70);
        Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 70f, 70f }), "a drag shows on each");
        ((IValueEditorContext<float>)opacity).ConfirmEdit(100, 80);

        Assert.Multiple(() =>
        {
            Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 80f, 80f }));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        });

        editor.HistoryManager.Undo();
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 100f, 40f }));
            Assert.That(Copy<RectShape>(tab).Opacity.Value, Is.EqualTo(100), "the undo shows the element again");
        });

        editor.HistoryManager.Redo();
        Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 80f, 80f }));
    }

    // The value shown is the first element's, so confirming it, or resetting to it, must still reach the others.
    [AvaloniaTest]
    public async Task ConfirmingTheShownValueWritesTheOthers()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        int before = editor.HistoryManager.UndoCount;

        ((IValueEditorContext<float>)opacity).ConfirmEdit(100, 100);
        Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 100f, 100f }));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));

        shapes[1].Opacity.Value = 30;
        editor.HistoryManager.Commit("test setup");
        HeadlessTestHelpers.Settle();
        opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        opacity.Reset();
        Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 100f, 100f }));
    }

    [AvaloniaTest]
    public async Task ObjectsAreAddedHiddenAndRemovedOnEveryElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var service = (IElementObjectService)editor.GetService(typeof(IElementObjectService))!;
        Element copy = tab.DisplayedElement.Value!;
        int before = editor.HistoryManager.UndoCount;

        service.Add(copy, new EllipseShape());
        Assert.Multiple(() =>
        {
            Assert.That(elements.Select(element => element.Objects[^1]), Is.All.TypeOf<EllipseShape>());
            Assert.That(elements[0].Objects[^1], Is.Not.SameAs(elements[1].Objects[^1]).And.Not.SameAs(copy.Objects[^1]));
            Assert.That(elements[0].Objects[^1].Id.Value, Is.EqualTo(copy.Objects[^1].Id.Value),
                "the element shown takes exact copies, so it stays equal to the copy");
            Assert.That(elements[1].Objects[^1].Id.Value, Is.Not.EqualTo(copy.Objects[^1].Id.Value));
            Assert.That(elements.Select(element => element.Objects[^1].TimeRange.Value),
                Is.EqualTo(elements.Select(element => element.Range)), "each element puts its own time on the object");
            Assert.That(elements.Select(element => element.Objects[^1].ZIndex.Value),
                Is.EqualTo(elements.Select(element => element.ZIndex.Value)));
            Assert.That(tab.Items, Has.Count.EqualTo(2));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        });

        tab.Items[1].IsEnabled.Value = false;
        Assert.That(elements.Select(element => element.Objects[^1].IsEnabled.Value), Is.All.False);

        service.Remove(copy, copy.Objects[^1]);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(elements.Select(element => element.Objects.Count), Is.All.EqualTo(1));
            Assert.That(tab.DisplayedElement.Value, Is.SameAs(copy), "edits through the copy keep it");
        });
    }

    [AvaloniaTest]
    public async Task EffectsTransformsAndBrushesChangeOnEveryElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[0].Fill.Value = new SolidColorBrush(Colors.White);
        shapes[1].Fill.Value = new SolidColorBrush(Colors.Red);
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        RectShape copy = Copy<RectShape>(tab);

        ((FilterEffectGroup)copy.FilterEffect.Value!).Children.Add(new Blur { Sigma = { Value = new Size(5, 5) } });
        ((TranslateTransform)((TransformGroup)copy.Transform.Value!).Children[0]).X.Value = 42;
        editor.HistoryManager.Commit("edit the copy");

        var fill = (BrushEditorViewModel)Editor(tab, copy.Fill);
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Green, 1));
        fill.SetValue(fill.Value.Value, gradient);
        ((LinearGradientBrush)copy.Fill.Value!).GradientStops[1].Color.Value = Colors.Yellow;
        editor.HistoryManager.Commit("edit a stop");

        Assert.Multiple(() =>
        {
            foreach (RectShape shape in shapes)
            {
                Assert.That(((FilterEffectGroup)shape.FilterEffect.Value!).Children.Single(), Is.TypeOf<Blur>());
                Assert.That(((Blur)((FilterEffectGroup)shape.FilterEffect.Value!).Children[0]).Sigma.Value, Is.EqualTo(new Size(5, 5)));
                Assert.That(((TranslateTransform)((TransformGroup)shape.Transform.Value!).Children[0]).X.Value, Is.EqualTo(42));
                Assert.That(shape.Fill.Value, Is.TypeOf<LinearGradientBrush>().And.Not.SameAs(copy.Fill.Value));
                Assert.That(((LinearGradientBrush)shape.Fill.Value!).GradientStops.Select(stop => stop.Color.Value),
                    Is.EqualTo(new[] { Colors.Blue, Colors.Yellow }));
            }

            Assert.That(shapes[0].Fill.Value, Is.Not.SameAs(shapes[1].Fill.Value));
        });
    }

    // Adding keyframes runs on each element's own values, at the same time from its start.
    [AvaloniaTest]
    public async Task KeyFramesAreAddedOnEachElementsOwnValues()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        Element copy = tab.DisplayedElement.Value!;
        var opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        int before = editor.HistoryManager.UndoCount;

        opacity.PrepareToEditAnimation();
        opacity.InsertKeyFrame(MediaTime.FromSeconds(0.5));
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(KeyFrames(shapes[0].Opacity), Is.EqualTo(new[] { (0.0, 100f), (0.5, 100f) }));
            Assert.That(KeyFrames(shapes[1].Opacity), Is.EqualTo(new[] { (0.0, 40f), (0.5, 40f) }),
                "the second element starts a second later and keeps its own value");
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 2));
            Assert.That(tab.DisplayedElement.Value, Is.SameAs(copy), "the element shown took what the copy did");
        });

        ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(MediaTime.FromSeconds(0.5));
        HeadlessTestHelpers.Settle();
        ((IValueEditorContext<float>)opacity).ConfirmEdit(100, 60);
        Assert.Multiple(() =>
        {
            Assert.That(KeyFrames(shapes[0].Opacity), Is.EqualTo(new[] { (0.0, 100f), (0.5, 60f) }));
            Assert.That(KeyFrames(shapes[1].Opacity), Is.EqualTo(new[] { (0.0, 40f), (0.5, 60f) }),
                "a keyframe's value goes to the keyframe at the same time");
        });

        opacity.RemoveKeyFrame(MediaTime.FromSeconds(0.5));
        Assert.That(shapes.Select(shape => shape.Opacity.GetKeyFrameAnimation()!.KeyFrames.Count), Is.All.EqualTo(1));
        opacity.RemoveAnimation();
        Assert.That(shapes.Select(shape => shape.Opacity.GetKeyFrameAnimation()), Is.All.Null);
    }

    [AvaloniaTest]
    public async Task ExpressionsAndSeparatedComponentsApplyToEveryElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].TransformOrigin.Value = RelativePoint.TopLeft;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        RectShape copy = Copy<RectShape>(tab);

        var opacity = (NumberEditorViewModel<float>)Editor(tab, copy.Opacity);
        Assert.That(opacity.SetExpression("1 + 2", out string? error), Is.True, error);
        Assert.That(shapes.Select(shape => shape.Opacity.Expression?.ExpressionString), Is.All.EqualTo("1 + 2"));
        opacity.RemoveExpression();
        Assert.That(shapes.Select(shape => shape.Opacity.HasExpression), Is.All.False);

        PropertyEditorViewModel origin = Editor(tab, copy.TransformOrigin);
        origin.SeparateComponents();
        Assert.Multiple(() =>
        {
            Assert.That(shapes.Select(shape => shape.TransformOrigin.GetPropertyAnimation()), Is.All.InstanceOf<IComponentAnimation>());
            Assert.That(shapes.Select(shape => shape.TransformOrigin.Evaluate(new EvaluationContext(shape.Start))),
                Is.EqualTo(new[] { RelativePoint.Center, RelativePoint.TopLeft }), "each separates its own value");
        });
        origin.UnseparateComponents();
        Assert.Multiple(() =>
        {
            Assert.That(shapes.Select(shape => shape.TransformOrigin.GetPropertyAnimation()), Is.All.Null);
            Assert.That(shapes.Select(shape => shape.TransformOrigin.Value),
                Is.EqualTo(new[] { RelativePoint.Center, RelativePoint.TopLeft }), "each keeps its own value");
        });
    }

    // A row of two properties runs its commands on the same row of each element.
    [AvaloniaTest]
    public async Task CombinedRowsRunTheirCommandsOnEveryElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Width.Value = 200;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);

        PropertyEditorViewModel size = Editor(tab, Copy<RectShape>(tab).Width);
        Assert.That(size, Is.InstanceOf<CombinedEditorViewModel<float, (float, float)>>());
        size.PrepareToEditAnimation();

        Assert.Multiple(() =>
        {
            Assert.That(KeyFrames(shapes[0].Width), Is.EqualTo(new[] { (0.0, 100f) }));
            Assert.That(KeyFrames(shapes[1].Width), Is.EqualTo(new[] { (0.0, 200f) }));
            Assert.That(shapes.Select(shape => shape.Height.GetKeyFrameAnimation()), Is.All.Not.Null);
        });
    }

    [AvaloniaTest]
    public async Task LockedElementsStayAsTheyAreAndAnEditableOneIsShown()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(
            () => new RectShape(), () => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Opacity.Value = 40;
        elements[0].IsLocked.Value = true;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        MultiElementEditSession session = tab.MultipleElements.Value!;

        Assert.Multiple(() =>
        {
            Assert.That(session.Shown, Is.SameAs(elements[1]));
            Assert.That(Copy<RectShape>(tab).Opacity.Value, Is.EqualTo(40));
            Assert.That(session.LockedNote.Value, Is.EqualTo(string.Format(MessageStrings.MultipleElements_LockedNote, 1)));
            Assert.That(tab.CanEdit.Value, Is.True);
        });

        var opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        ((IValueEditorContext<float>)opacity).ConfirmEdit(40, 70);
        Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.EqualTo(new[] { 100f, 70f, 70f }));
    }

    // Only the tab's edits go through the copy, so an edit made elsewhere changes one element and shows again.
    [AvaloniaTest]
    public async Task EditsMadeElsewhereReachOnlyTheirElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        Element copy = tab.DisplayedElement.Value!;

        shapes[0].Opacity.Value = 10;
        editor.HistoryManager.Commit("edit elsewhere");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(shapes[1].Opacity.Value, Is.EqualTo(100));
            Assert.That(tab.DisplayedElement.Value, Is.Not.SameAs(copy));
            Assert.That(Copy<RectShape>(tab).Opacity.Value, Is.EqualTo(10));
        });
    }

    [AvaloniaTest]
    public async Task TheTabShowsSeveralElementsWhileTheyAreSelectedAndItIsNotPinned()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        Shapes<RectShape>(elements)[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        IEditorSelection selection = Selection(editor);
        selection.Select(elements[0]);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = elements[0];
        HeadlessTestHelpers.Settle();
        Assert.That(tab.DisplayedElement.Value, Is.SameAs(elements[0]));

        selection.SelectedElements.Add(elements[1]);
        await HeadlessTestHelpers.WaitUntilAsync(() => tab.MultipleElements.Value != null);
        Assert.Multiple(() =>
        {
            Assert.That(tab.MultipleElements.Value!.Elements, Is.EqualTo(elements));
            Assert.That(tab.DisplayedElement.Value, Is.SameAs(tab.MultipleElements.Value.Copy.Value));
            Assert.That(tab.HasTarget.Value, Is.False, "several elements cannot be pinned");
        });

        // Clicking an element of the selection keeps the selection and shows that element.
        selection.Select(elements[1]);
        await HeadlessTestHelpers.WaitUntilAsync(() => tab.MultipleElements.Value?.Shown == elements[1]);
        Assert.That(Copy<RectShape>(tab).Opacity.Value, Is.EqualTo(40));

        selection.SelectedElements.Remove(elements[0]);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.MultipleElements.Value, Is.Null, "leaving does not wait");
        Assert.That(tab.DisplayedElement.Value, Is.SameAs(elements[0]), "the tab shows its element again");

        tab.IsPinned.Value = true;
        selection.SelectedElements.Add(elements[0]);
        await Task.Delay(300);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.MultipleElements.Value, Is.Null, "a pinned tab keeps its element");

        tab.IsPinned.Value = false;
        await HeadlessTestHelpers.WaitUntilAsync(() => tab.MultipleElements.Value != null);
    }

    // The tab shows the copy in the view it has for one element. A note above it appears only when an edit leaves an
    // element out.
    [AvaloniaTest]
    public async Task TheViewShowsTheCopyWithANoteOnlyWhenAnEditLeavesAnElementOut()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(
            () => new RectShape(), () => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var view = new ElementPropertyTabView { DataContext = tab };
        var window = new Window { Width = 480, Height = 640, Content = view };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(Banner(view).IsVisible, Is.False, "nothing to note while every element takes the edits");

            EngineObjectPropertyView item = window.GetVisualDescendants().OfType<EngineObjectPropertyView>().Single();
            ToggleButton visibility = item.FindControl<ToggleButton>("VisibilityButton")!;
            HeadlessTestHelpers.Click(visibility, ticks: 0);
            Assert.That(shapes.Select(shape => shape.IsEnabled.Value), Is.All.False);

            elements[2].IsLocked.Value = true;
            editor.HistoryManager.Commit("lock an element");
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(Banner(view).IsVisible, Is.True);
                Assert.That(Banner(view).GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                        .Single(text => text.Name == "lockedNoteText").Text,
                    Is.EqualTo(string.Format(MessageStrings.MultipleElements_LockedNote, 1)));
            });
        }
        finally { window.Close(); }
    }

    // A text and a shape share what a drawable has, which the tab shows of the text, without what only a text has.
    [AvaloniaTest]
    public async Task OnlyWhatEveryElementHoldsIsShown()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new TextBlock(), () => new RectShape());
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        TextBlock copy = Copy<TextBlock>(tab);

        Assert.Multiple(() =>
        {
            Assert.That(tab.Items.Single().TypeNames, Is.EqualTo(
                $"{TypeDisplayHelpers.GetLocalizedName(typeof(TextBlock))} / {TypeDisplayHelpers.GetLocalizedName(typeof(RectShape))}"));
            Assert.That(HasEditor(tab, copy.Text), Is.False, "only a text has text");
            Assert.That(HasEditor(tab, copy.Size), Is.False);
            Assert.That(HasEditor(tab, copy.Opacity), Is.True, "both are drawn with an opacity");
            Assert.That(HasEditor(tab, copy.Fill), Is.True, "both fill what they draw");
            Assert.That(tab.MultipleElements.Value!.NothingInCommonNote.Value, Is.Null);
        });

        var opacity = (NumberEditorViewModel<float>)Editor(tab, copy.Opacity);
        ((IValueEditorContext<float>)opacity).ConfirmEdit(100, 30);
        Assert.That(elements.Select(element => ((Drawable)element.Objects[0]).Opacity.Value), Is.All.EqualTo(30f));
    }

    // A shape and a sound share nothing a drawable or a sound has, so the tab says so instead of showing either.
    [AvaloniaTest]
    public async Task ElementsWithNothingInCommonShowANote()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new SourceSound());
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);

        Assert.Multiple(() =>
        {
            Assert.That(tab.Items.Select(item => item.IsShown.Value), Is.All.False);
            Assert.That(tab.MultipleElements.Value!.NothingInCommonNote.Value,
                Is.EqualTo(MessageStrings.MultipleElements_NothingInCommon));
        });
    }

    // Effects are paired by type: the second element's shadow is its first effect, and stands for the copy's second.
    [AvaloniaTest]
    public async Task EffectsArePairedByTypeAndOnlyThoseEveryElementHoldsAreShown()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        Effects(shapes[0]).Children.AddRange([new Blur(), new DropShadow()]);
        Effects(shapes[1]).Children.AddRange([new DropShadow(), new Saturate()]);
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        RectShape copy = Copy<RectShape>(tab);
        Element shownCopy = tab.DisplayedElement.Value!;

        Assert.That(EffectList(tab, copy).Items.Select(item => item.IsShown.Value), Is.EqualTo(new[] { false, true }),
            "the second element has no blur");

        ((DropShadow)Effects(copy).Children[1]).Position.Value = new Point(30, 30);
        editor.HistoryManager.Commit("edit the shadow");
        Assert.That(shapes.Select(shape => Effects(shape).Children.OfType<DropShadow>().Single().Position.Value),
            Is.All.EqualTo(new Point(30, 30)));

        Effects(copy).Children.Add(new InnerShadow());
        editor.HistoryManager.Commit("add an effect");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(EffectTypes(shapes[0]), Is.EqualTo(new[] { typeof(Blur), typeof(DropShadow), typeof(InnerShadow) }));
            Assert.That(EffectTypes(shapes[1]), Is.EqualTo(new[] { typeof(DropShadow), typeof(InnerShadow), typeof(Saturate) }),
                "a new effect goes after the one that stands for its neighbour");
            Assert.That(tab.DisplayedElement.Value, Is.SameAs(shownCopy), "an edit through the copy keeps it");
            Assert.That(EffectList(tab, copy).Items.Select(item => item.IsShown.Value), Is.EqualTo(new[] { false, true, true }));
        });

        Effects(copy).Children.Move(1, 2);
        editor.HistoryManager.Commit("move the shadow");
        Assert.Multiple(() =>
        {
            Assert.That(EffectTypes(shapes[0]), Is.EqualTo(new[] { typeof(Blur), typeof(InnerShadow), typeof(DropShadow) }));
            Assert.That(EffectTypes(shapes[1]), Is.EqualTo(new[] { typeof(InnerShadow), typeof(DropShadow), typeof(Saturate) }));
        });

        Effects(copy).Children.RemoveAt(2);
        editor.HistoryManager.Commit("remove the shadow");
        Assert.Multiple(() =>
        {
            Assert.That(EffectTypes(shapes[0]), Is.EqualTo(new[] { typeof(Blur), typeof(InnerShadow) }));
            Assert.That(EffectTypes(shapes[1]), Is.EqualTo(new[] { typeof(InnerShadow), typeof(Saturate) }));
        });
    }

    // The nodes and connections of a node graph refer to each other by Id and the tab does not show them, so an edit of
    // them stays with the element the copy was made from.
    [AvaloniaTest]
    public async Task NodeGraphEditsStayWithTheShownElement()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        foreach (RectShape shape in shapes)
            Effects(shape).Children.Add(new NodeGraphFilterEffect());
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        GraphModel copyGraph = Graph(Copy<RectShape>(tab));

        copyGraph.Nodes.Add(new OutputNode());
        editor.HistoryManager.Commit("add a node");

        Assert.Multiple(() =>
        {
            Assert.That(Graph(shapes[0]).Nodes.Select(node => node.Id.Value), Is.EqualTo(copyGraph.Nodes.Select(node => node.Id.Value)),
                "the element shown holds what the copy does");
            Assert.That(Graph(shapes[1]).Nodes, Is.Empty);
        });
    }

    // A locked element is not edited, so what it lacks does not keep the others from editing it.
    [AvaloniaTest]
    public async Task ALockedElementDoesNotHideWhatTheOthersShare()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(
            () => new RectShape(), () => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        Effects(shapes[0]).Children.Add(new Blur());
        Effects(shapes[2]).Children.Add(new Blur());
        elements[1].IsLocked.Value = true;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);

        Assert.That(EffectList(tab, Copy<RectShape>(tab)).Items.Select(item => item.IsShown.Value), Is.EqualTo(new[] { true }));
    }

    [AvaloniaTest]
    public async Task ValuesThatDifferAreMarkedAndCanBeMadeEqual()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        shapes[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        PropertyEditorViewModel opacity = Editor(tab, Copy<RectShape>(tab).Opacity);
        PropertyEditorViewModel size = Editor(tab, Copy<RectShape>(tab).Width);

        Assert.Multiple(() =>
        {
            Assert.That(opacity.ValueDifference.Value, Does.StartWith(MessageStrings.MultipleElements_ValuesDiffer)
                .And.Contain(": 100").And.Contain(": 40"));
            Assert.That(size.ValueDifference.Value, Is.Null, "both are as wide and as tall");
        });

        opacity.MatchOtherElements();
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(shapes.Select(shape => shape.Opacity.Value), Is.All.EqualTo(100f));
            Assert.That(Editor(tab, Copy<RectShape>(tab).Opacity).ValueDifference.Value, Is.Null);
        });
    }

    // A value goes where each element's own editor would put it: to its keyframe at the playhead, or else its next one.
    [AvaloniaTest]
    public async Task ValueEditsGoToTheKeyFrameEachElementEditsAtThePlayhead()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        Animate(shapes[0].Opacity, (0, 100), (0.8, 50));
        Animate(shapes[1].Opacity, (0, 40), (0.4, 20));
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);

        ((IValueEditorContext<float>)opacity).ConfirmEdit(100, 90);
        Assert.Multiple(() =>
        {
            Assert.That(KeyFrames(shapes[0].Opacity), Is.EqualTo(new[] { (0.0, 90f), (0.8, 50f) }),
                "an editor made with the playhead on the first keyframe edits it");
            Assert.That(KeyFrames(shapes[1].Opacity), Is.EqualTo(new[] { (0.0, 90f), (0.4, 20f) }));
        });

        ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(MediaTime.FromSeconds(0.2));
        HeadlessTestHelpers.Settle();
        opacity = (NumberEditorViewModel<float>)Editor(tab, Copy<RectShape>(tab).Opacity);
        ((IValueEditorContext<float>)opacity).ConfirmEdit(50, 60);
        Assert.Multiple(() =>
        {
            Assert.That(KeyFrames(shapes[0].Opacity), Is.EqualTo(new[] { (0.0, 90f), (0.8, 60f) }));
            Assert.That(KeyFrames(shapes[1].Opacity), Is.EqualTo(new[] { (0.0, 90f), (0.4, 60f) }),
                "the second element's next keyframe is at another time");
        });
    }

    [AvaloniaTest]
    public async Task TheMenuMarksAValueThatDiffers()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        Shapes<RectShape>(elements)[1].Opacity.Value = 40;
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        PropertyEditorViewModel opacity = Editor(tab, Copy<RectShape>(tab).Opacity);
        PropertyEditorViewModel size = Editor(tab, Copy<RectShape>(tab).Width);
        var window = new Window { Width = 480, Height = 900, Content = new ElementPropertyTabView { DataContext = tab } };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(Badge(window, opacity).IsVisible, Is.True);
                Assert.That(ToolTip.GetTip(Badge(window, opacity).FindAncestorOfType<Button>()!),
                    Is.EqualTo(opacity.ValueDifference.Value));
                Assert.That(Badge(window, size).IsVisible, Is.False);
            });
        }
        finally { window.Close(); }
    }

    // An effect that another element lacks takes no row in the view, whichever editor lists the effects.
    [AvaloniaTest]
    public async Task TheViewLeavesOutAnEffectAnotherElementLacks()
    {
        (EditViewModel editor, Element[] elements) = await OpenElements(() => new RectShape(), () => new RectShape());
        RectShape[] shapes = Shapes<RectShape>(elements);
        Effects(shapes[0]).Children.AddRange([new Blur(), new DropShadow()]);
        Effects(shapes[1]).Children.Add(new DropShadow());
        editor.HistoryManager.Commit("test setup");
        using ElementPropertyTabViewModel tab = OpenTab(editor, elements);
        var filter = (FilterEffectEditorViewModel)Editor(tab, Copy<RectShape>(tab).FilterEffect);
        filter.IsExpanded.Value = true;
        var window = new Window { Width = 480, Height = 900, Content = new ElementPropertyTabView { DataContext = tab } };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ListItemEditor[] rows = [.. window.GetVisualDescendants().OfType<ListItemEditor>()
                .Where(row => row.DataContext is ListItemEditorViewModel<FilterEffect>)];
            Assert.That(rows.Select(row => (((ListItemEditorViewModel<FilterEffect>)row.DataContext!).ItemAccessor.GetValue()?.GetType(), row.IsVisible)),
                Is.EqualTo(new[] { (typeof(Blur), false), (typeof(DropShadow), true) }));
        }
        finally { window.Close(); }
    }

    private static async Task<(EditViewModel Editor, Element[] Elements)> OpenElements(params Func<EngineObject>[] creates)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("multiple-elements");
        for (int i = 0; i < creates.Length; i++)
        {
            await TestShell.AddElementAsync(editor, creates[i], MediaTime.FromSeconds(i), MediaTime.FromSeconds(1), i);
        }

        return (editor, [.. scene.Children.OrderBy(element => element.Start.Value)]);
    }

    // A tab showing the elements, the first of them selected last.
    private static ElementPropertyTabViewModel OpenTab(EditViewModel editor, Element[] elements)
    {
        IEditorSelection selection = Selection(editor);
        selection.Select(elements[0]);
        foreach (Element element in elements.Skip(1))
        {
            selection.SelectedElements.Add(element);
        }

        var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = elements[0];
        HeadlessTestHelpers.Settle();
        Assert.That(tab.MultipleElements.Value?.Elements, Is.EqualTo(elements));
        return tab;
    }

    private static IEditorSelection Selection(EditViewModel editor)
        => (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;

    private static T[] Shapes<T>(Element[] elements) => [.. elements.Select(element => (T)(object)element.Objects[0])];

    private static T Copy<T>(ElementPropertyTabViewModel tab) => (T)(object)tab.DisplayedElement.Value!.Objects[0];

    // The editor of the copy's property among the tab's rows.
    private static PropertyEditorViewModel Editor(ElementPropertyTabViewModel tab, IProperty property)
        => PropertyEditorTestHost.Flatten(tab.Items.SelectMany(item => item.Properties), includeGroups: false)
            .OfType<PropertyEditorViewModel>()
            .First(editor => editor.EditedProperties.Any(adapter => ReferenceEquals(adapter.GetEngineProperty(), property)));

    private static bool HasEditor(ElementPropertyTabViewModel tab, IProperty property)
        => PropertyEditorTestHost.Flatten(tab.Items.SelectMany(item => item.Properties), includeGroups: false)
            .OfType<PropertyEditorViewModel>()
            .Any(editor => editor.EditedProperties.Any(adapter => ReferenceEquals(adapter.GetEngineProperty(), property)));

    private static FilterEffectGroup Effects(Drawable drawable) => (FilterEffectGroup)drawable.FilterEffect.Value!;

    private static Type[] EffectTypes(Drawable drawable) => [.. Effects(drawable).Children.Select(effect => effect.GetType())];

    private static GraphModel Graph(Drawable drawable)
        => ((NodeGraphFilterEffect)Effects(drawable).Children.Single()).Model.Value!;

    // The editor of the copy's effects, opened as the view opens it.
    private static ListEditorViewModel<FilterEffect> EffectList(ElementPropertyTabViewModel tab, Drawable copy)
    {
        var filter = (FilterEffectEditorViewModel)Editor(tab, copy.FilterEffect);
        filter.IsExpanded.Value = true;
        HeadlessTestHelpers.Settle();
        return filter.Group.Value!;
    }

    private static void Animate(IProperty<float> property, params (double Seconds, float Value)[] keyFrames)
    {
        var animation = new KeyFrameAnimation<float>();
        foreach ((double seconds, float value) in keyFrames)
        {
            animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(seconds) },
                Value = { Value = value }
            });
        }

        property.SetKeyFrameAnimation(animation);
    }

    // The bar above the properties that notes what an edit of several elements leaves out.
    private static Beutl.Controls.ToolTabBar Banner(ElementPropertyTabView view)
        => view.FindControl<ContentControl>("multipleElementsBanner")!
            .GetVisualDescendants().OfType<Beutl.Controls.ToolTabBar>().Single();

    // The mark of differing values on the menu of editor's row.
    private static Avalonia.Controls.Shapes.Ellipse Badge(Window window, PropertyEditorViewModel editor)
        => window.GetVisualDescendants().OfType<PropertyEditorMenu>()
            .Single(menu => ReferenceEquals(menu.DataContext, editor))
            .GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Single(ellipse => ellipse.Name == "valueDifferenceBadge");

    private static (double Seconds, float Value)[] KeyFrames(IProperty<float> property)
        => property.GetKeyFrameAnimation() is { } animation
            ? [.. animation.KeyFrames.Select(keyFrame => (keyFrame.KeyTime.Value.ToSeconds(), (float)keyFrame.Value!))]
            : [];
}
