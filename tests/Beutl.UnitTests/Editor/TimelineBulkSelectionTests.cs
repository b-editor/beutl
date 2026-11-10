using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.ProjectSystem;
using SelectionKind = Beutl.Editor.Components.TimelineTab.ViewModels.TimelineTabViewModel.ElementSelectionKind;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineBulkSelectionTests
{
    [TestCase(nameof(SelectionKind.Following), new[] { 1, 2, 3 })]
    [TestCase(nameof(SelectionKind.FollowingAllLayers), new[] { 1, 2, 3, 4, 5, 6 })]
    [TestCase(nameof(SelectionKind.Preceding), new[] { 0, 1 })]
    [TestCase(nameof(SelectionKind.PrecedingAllLayers), new[] { 0, 1, 4, 6 })]
    [TestCase(nameof(SelectionKind.Layer), new[] { 0, 1, 2, 3 })]
    [TestCase(nameof(SelectionKind.AtPosition), new[] { 1, 4, 6 })]
    [TestCase(nameof(SelectionKind.Group), new[] { 1, 4 })]
    [TestCase(nameof(SelectionKind.Invert), new[] { 1, 2, 4, 5, 6 })]
    public void Actions_replace_selection_without_changing_elements_or_editing_mode(string kind, int[] expected)
    {
        Scene scene = CreateScene();
        using TimelineTabViewModel model = TimelineTabViewModelTests.CreateViewModel(scene);
        model.SelectElement(model.GetViewModelFor(scene.Children[0])!);
        model.SelectElement(model.GetViewModelFor(scene.Children[3])!);
        model.IsRollMode.Value = true;
        MediaTime[] starts = [.. scene.Children.Select(element => element.Start.Value)];

        model.SelectElements(Enum.Parse<SelectionKind>(kind), MediaTime.FromSeconds(2.5), 0, scene.Children[1]);

        Assert.Multiple(() =>
        {
            Assert.That(model.SelectedElements.Select(item => item.Model),
                Is.EquivalentTo(expected.Select(index => scene.Children[index])));
            Assert.That(scene.Children.Select(element => element.Start.Value), Is.EqualTo(starts));
            Assert.That(model.IsRollMode.Value, Is.True);
        });
    }

    [Test]
    public void At_position_includes_starts_and_excludes_ends()
    {
        Scene scene = CreateScene();
        using TimelineTabViewModel model = TimelineTabViewModelTests.CreateViewModel(scene);

        model.SelectElements(SelectionKind.AtPosition, MediaTime.FromSeconds(2), 0);

        Assert.That(model.SelectedElements.Select(item => item.Model),
            Is.EquivalentTo(new[] { scene.Children[1], scene.Children[4], scene.Children[6] }));
    }

    [Test]
    public void Subframe_elements_use_the_exact_reference_position()
    {
        Scene scene = CreateScene();
        scene.Children[1].Length.Value = MediaTime.FromSeconds(0.025);
        using TimelineTabViewModel model = TimelineTabViewModelTests.CreateViewModel(scene);

        model.SelectElements(SelectionKind.Following, MediaTime.FromSeconds(2.02), 0);

        Assert.That(model.SelectedElements.Select(item => item.Model),
            Is.EquivalentTo(scene.Children.Skip(1).Take(3)));
    }

    [Test]
    public void Group_selection_uses_selected_groups_on_the_background_and_refreshes_membership()
    {
        Scene scene = CreateScene();
        using TimelineTabViewModel model = TimelineTabViewModelTests.CreateViewModel(scene);
        model.SelectElement(model.GetViewModelFor(scene.Children[0])!);
        model.SelectElement(model.GetViewModelFor(scene.Children[3])!);
        Assert.That(model.CanSelectGroup(null), Is.True);
        Assert.That(model.CanSelectGroup(scene.Children[3]), Is.False);

        model.SelectElements(SelectionKind.Group, MediaTime.Zero, 0);

        Assert.That(model.SelectedElements.Select(item => item.Model),
            Is.EquivalentTo(new[] { scene.Children[0], scene.Children[5] }));
        scene.Groups.Clear();
        Assert.That(model.CanSelectGroup(null), Is.False);
    }

    private static Scene CreateScene()
    {
        var scene = new Scene();
        scene.Duration.Value = MediaTime.FromSeconds(10);
        scene.Children.AddRange(new[]
        {
            Describe(0, 2, 0), Describe(2, 1, 0), Describe(4, 1, 0), Describe(100, 1, 0),
            Describe(1, 2, 1), Describe(5, 1, 1), Describe(2, 2, 2)
        });
        scene.Children[2].IsLocked.Value = true;
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        scene.Groups.Add([scene.Children[1].Id.Value, scene.Children[4].Id.Value]);
        scene.Groups.Add([scene.Children[0].Id.Value, scene.Children[5].Id.Value]);
        return scene;

        static Element Describe(double start, double length, int layer) => new()
        {
            Start = { Value = MediaTime.FromSeconds(start) },
            Length = { Value = MediaTime.FromSeconds(length) },
            ZIndex = { Value = layer }
        };
    }
}
