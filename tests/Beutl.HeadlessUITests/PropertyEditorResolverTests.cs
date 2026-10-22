using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
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
    public void A_match_editor_without_a_context_passes_the_property_to_the_next_editor()
    {
        var extension = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(
            _ => null!, _ => new Border(), new PropertyEditorOptions { Match = _ => true }));

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, extension]);

        Assert.That(context, Is.TypeOf<StringEditorViewModel>());
    }

    [AvaloniaTest]
    public void A_match_editor_without_a_context_is_asked_once_and_its_extension_s_next_editor_takes_the_property()
    {
        int asked = 0;
        var extension = new TestEditors(r =>
        {
            r.Register<string?, ValueEditorViewModel<string?>>(
                _ => { asked++; return null!; }, _ => new Border(), new PropertyEditorOptions { Match = _ => true });
            r.Register<string?, TestValueEditor>(p => new(p), _ => new Border(), new PropertyEditorOptions { Match = _ => true });
        });

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, extension]);

        Assert.That(context, Is.TypeOf<TestValueEditor>());
        Assert.That(asked, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void A_match_that_returns_a_property_it_was_not_given_takes_nothing()
    {
        var effect = new SKSLScriptEffect();
        IPropertyAdapter foreign = PropertyAdapterFactory.CreateAdapter(effect.Script, effect);
        var extension = new TestEditors(r => r.RegisterGroup<ValueEditorViewModel<string?>>(
            _ => [foreign], properties => new((IPropertyAdapter<string?>)properties[0]), _ => new Border()));

        using IPropertyEditorContext? context = Resolve([PropertyEditorExtension.Instance, extension]);

        Assert.That(context, Is.TypeOf<StringEditorViewModel>());
    }

    [AvaloniaTest]
    public void A_registration_without_a_node_control_leaves_nodes_to_the_next_editor()
    {
        var extension = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(p => new(p), _ => new Border()));

        using IPropertyEditorContext? node = Resolve([PropertyEditorExtension.Instance, extension], PropertyEditorSurface.Node);

        Assert.That(node, Is.TypeOf<StringEditorViewModel>());
    }

    [AvaloniaTest]
    public void A_registration_with_a_node_control_shows_in_nodes()
    {
        var extension = new TestEditors(r => r.Register<string?, ValueEditorViewModel<string?>>(
            p => new(p), _ => new Border(), new PropertyEditorOptions { CreateNodeControl = _ => new Border() }));

        using IPropertyEditorContext? node = Resolve([PropertyEditorExtension.Instance, extension], PropertyEditorSurface.Node);

        Assert.That(node, Is.TypeOf<ValueEditorViewModel<string?>>());
    }

    [AvaloniaTest]
    public void A_value_editor_shows_in_nodes()
    {
        var extension = new TestEditors(r => r.RegisterValue<string?, string>(p => new TestValueEditor(p), () => new StringEditor()));

        using IPropertyEditorContext? node = Resolve([PropertyEditorExtension.Instance, extension], PropertyEditorSurface.Node);

        Assert.That(node, Is.TypeOf<TestValueEditor>());
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

    private sealed class TestValueEditor(IPropertyAdapter<string?> property) : PropertyEditorViewModel<string?, string>(property)
    {
        protected override string ToEdit(string? value) => value ?? string.Empty;

        protected override string? FromEdit(string edit) => edit;
    }
}
