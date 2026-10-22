using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.HeadlessUITests;

// The order in which the resolver asks the registrations of the extensions.
[TestFixture]
public sealed class PropertyEditorResolverTests
{
    [AvaloniaTest]
    public void A_later_loaded_extension_takes_a_type_before_the_built_in_editor()
    {
        var extension = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(p => new(p), _ => new Border()));

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, extension]);

        Assert.That(context, Is.TypeOf<ValueEditorViewModel<string?>>());
    }

    [AvaloniaTest]
    public void A_null_context_passes_the_property_to_the_next_registration()
    {
        var extension = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(_ => null!, _ => new Border()));

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, extension]);

        Assert.That(context, Is.TypeOf<StringEditorViewModel>());
    }

    [AvaloniaTest]
    public void An_editor_that_matches_the_property_goes_before_a_later_extension_that_takes_its_type()
    {
        var matching = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(
            p => new(p), _ => new Border(), new PropertyEditorOptions { Match = _ => true }));
        var typed = new TestEditors(r => r.Register<string?, StringEditorViewModel>(p => new(p), _ => new Border()));

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, matching, typed]);

        Assert.That(context, Is.TypeOf<ValueEditorViewModel<string?>>());
    }

    [AvaloniaTest]
    public void The_script_editor_is_only_in_the_panel()
    {
        PropertyEditorExtension[] extensions = [PropertyEditorExtension.Instance, ScriptEditorExtension.Instance];

        using IPropertyEditorContext? panel = Resolve(extensions);
        using IPropertyEditorContext? node = Resolve(extensions, PropertyEditorSurface.Node);

        Assert.That(panel, Is.TypeOf<ScriptEditorViewModel>());
        Assert.That(node, Is.TypeOf<StringEditorViewModel>());
    }

    private static IPropertyEditorContext? Resolve(
        PropertyEditorExtension[] extensions, PropertyEditorSurface surface = PropertyEditorSurface.Panel)
    {
        var effect = new SKSLScriptEffect();
        return PropertyEditorResolver.CreateContext(PropertyAdapterFactory.CreateAdapter(effect.Script, effect), extensions, surface);
    }

    private sealed class TestEditors(Action<PropertyEditorRegistry> register) : PropertyEditorExtension
    {
        protected internal override void RegisterEditors(PropertyEditorRegistry registry) => register(registry);
    }
}
