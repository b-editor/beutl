using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using EngineTextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class StringEditorTests
{
    // One line with no line break, far longer than the 320-pixel host is wide.
    private const string LongLine =
        "A single line of text that runs well past the width of the property editor and keeps on going";

    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.Normal)]
    [TestCase(PropertyEditorStyle.Compact)]
    [TestCase(PropertyEditorStyle.Settings)]
    public void A_multiline_editor_wraps_a_long_line_at_its_width(PropertyEditorStyle style)
    {
        var editor = new StringEditor { Header = "Text", EditorStyle = style, Text = LongLine, Classes = { "multiline" } };
        using var host = new EditorTestHost<StringEditor>(editor, width: 320, height: 400);
        if (style == PropertyEditorStyle.Settings)
        {
            // The settings layout keeps the text box in the item's expandable area.
            host.RequireDescendant<OptionsDisplayItem>().IsExpanded = true;
            HeadlessTestHelpers.Render(3);
        }

        Assert.That(PresenterOf(host).TextLayout.TextLines, Has.Count.GreaterThan(1),
            "a line longer than the editor should continue on the next line instead of running out of view");
    }

    [AvaloniaTest]
    public void A_single_line_editor_keeps_a_long_line_on_one_line()
    {
        var editor = new StringEditor { Header = "Name", Text = LongLine };
        using var host = new EditorTestHost<StringEditor>(editor, width: 320);

        Assert.That(PresenterOf(host).TextLayout.TextLines, Has.Count.EqualTo(1));
    }

    // Four lines of indented code, the third longer than the 320-pixel host is wide.
    private const string CodeText =
        "void main()\n{\n\toutColor = texture(srcTexture, fragCoord) * vec4(progress, progress, progress, 1.0);\n}";

    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.Normal)]
    [TestCase(PropertyEditorStyle.Compact)]
    public void A_code_editor_keeps_its_lines_as_written(PropertyEditorStyle style)
    {
        var editor = new StringEditor { Header = "Code", EditorStyle = style, Text = CodeText, Classes = { "multiline", "code" } };
        using var host = new EditorTestHost<StringEditor>(editor, width: 320, height: 400);

        Assert.That(PresenterOf(host).TextLayout.TextLines, Has.Count.EqualTo(4),
            "a long line of code should scroll sideways instead of wrapping");
    }

    [AvaloniaTest]
    public void A_code_editor_lines_up_its_columns()
    {
        // Ten narrow letters and ten wide ones take the same width only in a monospace face.
        double[] code = LineWidths("iiiiiiiiii\nMMMMMMMMMM", "multiline", "code");
        double[] prose = LineWidths("iiiiiiiiii\nMMMMMMMMMM", "multiline");

        Assert.That(code[0], Is.EqualTo(code[1]).Within(0.5), "code should be set in a monospace face");
        Assert.That(prose[1] - prose[0], Is.GreaterThan(10), "prose should keep the proportional UI face");
    }

    // A node shows its fields in the compact layout, where Tab used to move focus even in code (#4030).
    [AvaloniaTest]
    public void Tab_indents_code_on_a_node()
    {
        var editor = new StringEditor { Header = "Code", EditorStyle = PropertyEditorStyle.Compact, Classes = { "multiline", "code" } };
        using var host = new EditorTestHost<StringEditor>(editor);

        TextBox box = PressTabIn(host);

        Assert.That(box.Text, Is.EqualTo("\t"));
        Assert.That(box.IsFocused, Is.True, "Tab should indent the code rather than move focus");
    }

    [AvaloniaTest]
    public void Tab_moves_on_from_prose_on_a_node()
    {
        var editor = new StringEditor { Header = "Text", EditorStyle = PropertyEditorStyle.Compact, Classes = { "multiline" } };
        using var host = new EditorTestHost<StringEditor>(editor);

        TextBox box = PressTabIn(host);

        Assert.That(box.Text, Is.Null.Or.Empty);
        Assert.That(box.IsFocused, Is.False, "prose should keep Tab for moving between fields");
    }

    [AvaloniaTest]
    public void Shift_Tab_moves_back_from_code_on_a_node()
    {
        var editor = new StringEditor { Header = "Code", EditorStyle = PropertyEditorStyle.Compact, Classes = { "multiline", "code" } };
        using var host = new EditorTestHost<StringEditor>(editor);
        var previous = new Button { Content = "previous" };
        ((Panel)editor.Parent!).Children.Insert(0, previous);

        TextBox box = PressTabIn(host, RawInputModifiers.Shift);

        Assert.That(box.Text, Is.Null.Or.Empty, "Shift+Tab should not type a tab");
        Assert.That(previous.IsFocused, Is.True, "Shift+Tab should leave the code for the field before it");
    }

    [AvaloniaTest]
    public void Code_fields_get_the_code_editor_and_prose_keeps_the_multiline_one()
    {
        var expression = new ExpressionNode();
        var fullscreen = new ShaderFullscreenPassNode();
        var glsl = new GLSLScriptEffect();
        var text = new EngineTextBlock();
        INodeMember fullscreenCode = fullscreen.Items.Single(member => member.Name.Value == fullscreen.Code.Name);

        Assert.Multiple(() =>
        {
            Assert.That(EditorClassesOf(expression.Expression.Property!), Is.EquivalentTo(new[] { "multiline", "code" }),
                "the expression node's script");
            Assert.That(EditorClassesOf(fullscreenCode.Property!), Is.EquivalentTo(new[] { "multiline", "code" }),
                "a shader pass node's code");
            Assert.That(EditorClassesOf(new EnginePropertyAdapter<string>(glsl.FragmentShader, glsl)),
                Is.EquivalentTo(new[] { "multiline", "code" }), "a script effect's script");
            Assert.That(EditorClassesOf(new EnginePropertyAdapter<string?>(text.Text, text)),
                Is.EquivalentTo(new[] { "multiline" }), "a text block's text");
        });
    }

    private static TextPresenter PresenterOf(EditorTestHost<StringEditor> host)
    {
        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        TextPresenter? presenter = box.FindDescendantOfType<TextPresenter>();
        Assert.That(presenter, Is.Not.Null, "The text box has no text presenter.");
        return presenter!;
    }

    private static double[] LineWidths(string text, params string[] classes)
    {
        var editor = new StringEditor { Header = "Text", Text = text };
        editor.Classes.AddRange(classes);
        using var host = new EditorTestHost<StringEditor>(editor);
        return [.. PresenterOf(host).TextLayout.TextLines.Select(line => line.Width)];
    }

    private static TextBox PressTabIn(EditorTestHost<StringEditor> host, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        box.Focus();
        HeadlessTestHelpers.Settle();
        host.Window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "\t");
        HeadlessTestHelpers.Settle();
        return box;
    }

    private static string[] EditorClassesOf(IPropertyAdapter property)
    {
        using var model = new StringEditorViewModel((IPropertyAdapter<string?>)property);
        var editor = new StringEditor();
        model.Accept(editor);
        return [.. editor.Classes.Where(name => name is "multiline" or "code")];
    }

    [AvaloniaTest]
    public void Typing_updates_text_and_raises_value_changed_while_focused()
    {
        var editor = new StringEditor { Header = "Name" };
        using var host = new EditorTestHost<StringEditor>(editor);

        var changed = new List<string>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "hello");

        Assert.That(editor.Text, Is.EqualTo("hello"));
        Assert.That(changed, Is.Not.Empty);
        Assert.That(changed[^1], Is.EqualTo("hello"));
    }

    [AvaloniaTest]
    public void Losing_focus_after_an_edit_raises_value_confirmed()
    {
        var editor = new StringEditor { Header = "Name" };
        using var host = new EditorTestHost<StringEditor>(editor);

        var confirmed = new List<string>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "world");
        Assert.That(confirmed, Is.Empty, "Confirm should only fire on focus loss.");

        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { "world" }));
    }

    [AvaloniaTest]
    public void Focus_loss_without_a_change_does_not_confirm()
    {
        var editor = new StringEditor { Header = "Name", Text = "initial" };
        using var host = new EditorTestHost<StringEditor>(editor);

        bool confirmed = false;
        editor.ValueConfirmed += (_, _) => confirmed = true;

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        box.Focus();
        HeadlessTestHelpers.Settle();
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.False);
    }
}
