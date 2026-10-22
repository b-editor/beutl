using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NestedEditorStateTests
{
    // The group creates its item editors when it expands, so their state is read after that.
    [AvaloniaTest]
    public async Task An_effect_in_an_expanded_group_comes_back_expanded()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"nested-state-{Guid.NewGuid():N}");
        var blur = new Blur();
        Element shape = NewElement(new RectShape { FilterEffect = { Value = new FilterEffectGroup { Children = { blur } } } });
        Element other = NewElement(new EllipseShape());
        editor.Scene.Children.Add(shape);
        editor.Scene.Children.Add(other);
        using var model = new ElementPropertyTabViewModel(editor);
        model.Element.Value = shape;
        GroupEditor(model).IsExpanded.Value = true;
        BlurEditor(model).IsExpanded.Value = true;

        model.Element.Value = other;
        model.Element.Value = shape;

        Assert.Multiple(() =>
        {
            Assert.That(GroupEditor(model).IsExpanded.Value, Is.True);
            Assert.That(BlurEditor(model).IsExpanded.Value, Is.True);
        });
    }

    private static Element NewElement(Beutl.Engine.EngineObject content)
    {
        var element = new Element
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"nested-state-{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(content);
        return element;
    }

    private static FilterEffectEditorViewModel GroupEditor(ElementPropertyTabViewModel model)
        => PropertyEditorTestHost.Flatten(model.Items.Single().Properties, includeGroups: false)
            .OfType<FilterEffectEditorViewModel>()
            .Single();

    private static FilterEffectEditorViewModel BlurEditor(ElementPropertyTabViewModel model)
        => (FilterEffectEditorViewModel)GroupEditor(model).Group.Value!.Items.Single().Context!;
}
