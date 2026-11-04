using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Services.Tutorials;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AnimationEditTutorialTests
{
    // A new drawable's transform section starts closed, and the tutorial's next step points at the X editor in it.
    [AvaloniaTest]
    public async Task The_step_after_the_selection_opens_the_section_down_to_the_translate()
    {
        await TestReset.ResetShellAsync();
        try
        {
            TutorialDefinition tutorial = AnimationEditTutorial.Create(TestShell.Editor, TestShell.Project);
            Assert.That(await tutorial.FulfillPrerequisites!(), Is.True);
            EditViewModel editor = TutorialHelpers.GetEditViewModel(TestShell.Editor)!;
            Element element = editor.Scene.Children.Single();
            editor.GetRequiredService<IEditorSelection>().Select(element);
            HeadlessTestHelpers.Settle();

            TransformEditorViewModel section = PropertyEditorTestHost.Flatten(
                    editor.FindToolTab<ElementPropertyTabViewModel>(tab => tab.Element.Value == element)!.Items.Single().Properties,
                    includeGroups: false)
                .OfType<TransformEditorViewModel>()
                .Single();
            Assert.That(section.IsExpanded.Value, Is.False);

            tutorial.Steps.Single(step => step.Id == "animation-add-translate").OnShown!();
            HeadlessTestHelpers.Settle();

            var group = (TransformGroup)element.Objects.OfType<EllipseShape>().Single().Transform.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(section.IsExpanded.Value, Is.True);
                var row = (TransformEditorViewModel)section.Group.Value!.Items[0].Context!;
                Assert.That(row.Value.Value, Is.SameAs(group.Children[0]));
                Assert.That(row.IsExpanded.Value, Is.True);
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }
}
