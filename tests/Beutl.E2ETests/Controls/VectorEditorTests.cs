using System.Globalization;
using System.Reactive.Disposables;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class VectorEditorTests
{
    [AvaloniaTest]
    public void Vector2_editing_one_component_updates_only_that_component()
    {
        var editor = new Vector2Editor<int> { Header = "Size" };
        using var host = new EditorTestHost<Vector2Editor<int>>(editor);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        TextBox second = host.Require<TextBox>("PART_InnerSecondTextBox");

        host.TypeInto(first, "12");
        host.TypeInto(second, "34");

        Assert.That(editor.FirstValue, Is.EqualTo(12));
        Assert.That(editor.SecondValue, Is.EqualTo(34));
    }

    [AvaloniaTest]
    public void Vector2_focus_loss_confirms_the_composed_pair()
    {
        var editor = new Vector2Editor<int> { Header = "Size" };
        using var host = new EditorTestHost<Vector2Editor<int>>(editor);

        var confirmed = new List<(int, int)>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<(int, int)>)e).NewValue);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        host.TypeInto(first, "7");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.Not.Empty);
        Assert.That(confirmed[^1].Item1, Is.EqualTo(7));
    }

    [AvaloniaTest]
    public void Vector2_wheel_changes_the_hovered_component()
    {
        var editor = new Vector2Editor<int> { Header = "Size", LargeChange = 10 };
        using var host = new EditorTestHost<Vector2Editor<int>>(editor);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        host.TypeInto(first, "2");
        host.WheelOver(first, deltaY: 1);

        Assert.That(editor.FirstValue, Is.EqualTo(12));
        Assert.That(editor.SecondValue, Is.EqualTo(0));
    }

    [AvaloniaTest]
    public void Vector3_editing_all_three_components_composes_the_value()
    {
        var editor = new Vector3Editor<float> { Header = "XYZ" };
        using var host = new EditorTestHost<Vector3Editor<float>>(editor);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), 1.5f.ToString(CultureInfo.CurrentCulture));
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), 2.5f.ToString(CultureInfo.CurrentCulture));
        host.TypeInto(host.Require<TextBox>("PART_InnerThirdTextBox"), 3.5f.ToString(CultureInfo.CurrentCulture));

        Assert.That(editor.FirstValue, Is.EqualTo(1.5f));
        Assert.That(editor.SecondValue, Is.EqualTo(2.5f));
        Assert.That(editor.ThirdValue, Is.EqualTo(3.5f));
    }

    [AvaloniaTest]
    public void Vector4_editing_all_four_components_composes_the_value()
    {
        var editor = new Vector4Editor<float> { Header = "XYZW" };
        using var host = new EditorTestHost<Vector4Editor<float>>(editor);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "1");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "2");
        host.TypeInto(host.Require<TextBox>("PART_InnerThirdTextBox"), "3");
        host.TypeInto(host.Require<TextBox>("PART_InnerFourthTextBox"), "4");

        Assert.That(editor.FirstValue, Is.EqualTo(1f));
        Assert.That(editor.SecondValue, Is.EqualTo(2f));
        Assert.That(editor.ThirdValue, Is.EqualTo(3f));
        Assert.That(editor.FourthValue, Is.EqualTo(4f));
    }

    [AvaloniaTest]
    public void RelativePoint_editing_components_confirms_a_composed_relative_point()
    {
        var editor = new RelativePointEditor { Header = "Origin", Unit = RelativeUnit.Absolute };
        using var host = new EditorTestHost<RelativePointEditor>(editor);

        var confirmed = new List<RelativePoint>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<RelativePoint>)e).NewValue);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "10");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "20");
        host.MoveFocusToSink();

        Assert.That(editor.FirstValue, Is.EqualTo(10f));
        Assert.That(editor.SecondValue, Is.EqualTo(20f));
        Assert.That(confirmed, Is.Not.Empty);
        Assert.That(confirmed[^1], Is.EqualTo(new RelativePoint(10, 20, RelativeUnit.Absolute)));
    }

    [AvaloniaTest]
    public void Reapplying_the_template_releases_the_old_parts_subscriptions()
    {
        AssertTemplateReappliesWithoutLeftovers(new Vector2Editor<float> { Header = "XY" });
        AssertTemplateReappliesWithoutLeftovers(new Vector3Editor<float> { Header = "XYZ" });
        AssertTemplateReappliesWithoutLeftovers(new Vector4Editor<float> { Header = "XYZW" });
    }

    private static void AssertTemplateReappliesWithoutLeftovers<TEditor>(TEditor editor)
        where TEditor : PropertyEditor
    {
        using var host = new EditorTestHost<TEditor>(editor);
        int[] applied = PartSubscriptionCounts(editor);
        // The generic editor and its base each subscribe to template parts. Anything else means the template was
        // not applied yet or the field moved, and the comparison below would prove nothing.
        Assert.That(editor.Template, Is.Not.Null, typeof(TEditor).Name);
        Assert.That(applied, Has.Length.EqualTo(2), typeof(TEditor).Name);
        Assert.That(applied, Is.All.GreaterThan(0), typeof(TEditor).Name);

        var template = editor.Template;
        editor.Template = null;
        editor.ApplyTemplate();
        editor.Template = template;
        editor.ApplyTemplate();
        HeadlessTestHelpers.Settle();

        Assert.That(PartSubscriptionCounts(editor), Is.EqualTo(applied), typeof(TEditor).Name);
    }

    // Each class in the editor's hierarchy keeps its template-part subscriptions in its own _disposables.
    private static int[] PartSubscriptionCounts(TemplatedControl editor)
    {
        var counts = new List<int>();
        for (Type? type = editor.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.GetValue(editor) is CompositeDisposable disposables)
                counts.Add(disposables.Count);
        }

        return [.. counts];
    }
}
