using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.NUnit;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PropertyBindingTests
{
    [AvaloniaTest]
    public void Two_way_binding_updates_both_the_editor_and_reactive_property()
    {
        using var value = new ReactiveProperty<string?>("initial");
        var editor = new TextBox();
        using var binding = editor.Bind(TextBox.TextProperty, value.ToPropertyBinding(BindingMode.TwoWay));

        Assert.That(editor.Text, Is.EqualTo("initial"));

        value.Value = "model update";
        HeadlessTestHelpers.Settle();
        Assert.That(editor.Text, Is.EqualTo("model update"));

        editor.SetCurrentValue(TextBox.TextProperty, "editor update");
        HeadlessTestHelpers.Settle();
        Assert.That(value.Value, Is.EqualTo("editor update"));
    }

    [AvaloniaTest]
    public void One_way_binding_does_not_write_editor_changes_to_the_reactive_property()
    {
        using var value = new ReactiveProperty<string?>("initial");
        var editor = new TextBox();
        using var binding = editor.Bind(TextBox.TextProperty, value.ToPropertyBinding(BindingMode.OneWay));

        editor.SetCurrentValue(TextBox.TextProperty, "editor update");
        HeadlessTestHelpers.Settle();
        Assert.That(value.Value, Is.EqualTo("initial"));

        value.Value = "model update";
        HeadlessTestHelpers.Settle();
        Assert.That(editor.Text, Is.EqualTo("model update"));
    }
}
