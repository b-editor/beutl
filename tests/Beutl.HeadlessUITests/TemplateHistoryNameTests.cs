using Avalonia.Headless.NUnit;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

// Applying a template to an effect, whether it replaces the effect or joins a group, leaves one history entry
// labelled with the "Apply template" command name.
[TestFixture]
public sealed class TemplateHistoryNameTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Applying_a_template_to_a_filter_effect_is_labelled_apply_template(bool group)
    {
        var shape = new RectShape();
        shape.FilterEffect.Value = group ? new FilterEffectGroup() : new Brightness();
        var element = new Element { Uri = new Uri("file:///template-history.belm") };
        element.AddObject(shape);
        using var history = new HistoryScope(element);
        using var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(new ServiceVisitor(
            (typeof(Element), element),
            (typeof(HistoryManager), history.History),
            (typeof(Beutl.Api.Services.ExtensionProvider), TestShell.Extensions),
            (typeof(IPropertyEditorFactory), new Beutl.Editor.Components.PropertyEditors.Services.PropertyEditorFactoryAdapter(TestShell.Extensions))));
        int entries = history.History.Entries.Count;

        bool applied = viewModel.ApplyTemplate(ObjectTemplateItem.CreateFromInstance(new Blur(), "Blur"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(history.History.Entries, Has.Count.EqualTo(entries + 1));
            Assert.That(history.History.Entries[^1].DisplayLabel, Is.EqualTo(CommandNames.ApplyTemplate));
        });
    }
}
