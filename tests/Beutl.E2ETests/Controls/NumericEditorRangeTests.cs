using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeRect = Beutl.Graphics.RelativeRect;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.E2ETests.Controls;

[TestFixture, NonParallelizable]
public class NumericEditorRangeTests
{
    public sealed record EditorCase(string Kind, bool Uniform = false)
    {
        public override string ToString() => Uniform ? $"{Kind}-uniform" : Kind;
    }

    private static readonly EditorCase[] s_editors =
    [
        new("Number"),
        new("Vector2"), new("Vector2", true),
        new("Vector3"), new("Vector3", true),
        new("Vector4"), new("Vector4", true),
        new("RelativePoint"), new("RelativePoint", true),
        new("RelativeRect"), new("RelativeRect", true),
    ];

    private static IEnumerable<EditorCase> NumberAndVectorEditors => s_editors.Where(x => !x.Kind.StartsWith("Relative"));
    private static IEnumerable<EditorCase> RelativeEditors => s_editors.Where(x => x.Kind.StartsWith("Relative"));
    private static IEnumerable<EditorCase> SeparateComponents => s_editors.Where(x => x.Kind != "Number" && !x.Uniform);

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Each_component_preserves_partial_input_and_confirms_the_composed_value(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        object? oldValue = null;
        editor.Host.Editor.ValueConfirmed += (_, e) => oldValue = e.GetType().GetProperty("OldValue")!.GetValue(e);
        for (int index = 0; index < editor.VisibleComponentCount; index++)
        {
            float[] initial = editor.Reset(100);
            TextBox box = editor.TextBox(index);
            editor.Host.TypeInto(box, "7");

            Assert.That(box.Text, Is.EqualTo("7"));
            Assert.That(box.CaretIndex, Is.EqualTo(1));
            Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 50)));
            Assert.That(editor.Confirmed, Is.Empty);

            editor.Host.Window.KeyTextInput("0");
            HeadlessTestHelpers.Settle();
            float[] expected = editor.WithEditedValue(initial, index, 70);
            Assert.That(box.Text, Is.EqualTo("70"));
            Assert.That(editor.Values, Is.EqualTo(expected));

            editor.Host.MoveFocusToSink();
            editor.AssertTextMatchesValues();
            Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(expected) }));
            Assert.That(oldValue, Is.EqualTo(editor.Compose(initial)));
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Focus_loss_displays_bounded_values_even_when_the_value_is_unchanged(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        foreach ((float start, string input, float boundary) in new[]
                 { (100f, "7", 50f), (50f, "7", 50f), (100f, "300", 200f), (200f, "300", 200f) })
        {
            for (int index = 0; index < editor.VisibleComponentCount; index++)
            {
                float[] initial = editor.Reset(start);
                TextBox box = editor.TextBox(index);
                editor.Host.TypeInto(box, input);
                Assert.That(box.Text, Is.EqualTo(input));
                float[] expected = editor.WithEditedValue(initial, index, boundary);
                Assert.That(editor.Values, Is.EqualTo(expected));

                editor.Host.MoveFocusToSink();
                editor.AssertTextMatchesValues();
                Assert.That(editor.Confirmed, Is.EqualTo(start == boundary ? [] : new[] { editor.Compose(expected) }));
            }
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Wheel_applies_bounds_and_shift_step_before_focus_loss(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        for (int index = 0; index < editor.VisibleComponentCount; index++)
        {
            float[] initial = editor.Reset(195);
            TextBox box = editor.TextBox(index);
            editor.Host.WheelOver(box, 1);
            Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 200)));
            editor.AssertTextMatchesValues();
            Assert.That(editor.Confirmed, Is.Empty);

            editor.Host.WheelOver(box, -1);
            Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 190)));
            editor.AssertTextMatchesValues();

            editor.Host.WheelOver(box, 0, deltaX: -1, modifiers: RawInputModifiers.Shift);
            float[] expected = editor.WithEditedValue(initial, index, 191);
            Assert.That(editor.Values, Is.EqualTo(expected));
            editor.AssertTextMatchesValues();
            editor.Host.MoveFocusToSink();
            Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(expected) }));

            initial = editor.Reset(50);
            editor.Host.WheelOver(box, -1);
            Assert.That(editor.Values, Is.EqualTo(initial));
            editor.AssertTextMatchesValues();
            editor.Host.MoveFocusToSink();
            Assert.That(editor.Confirmed, Is.Empty);
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(NumberAndVectorEditors))]
    public void Header_drag_clamps_live_values_and_confirms_once_on_release(EditorCase editorCase)
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool pointerLock = config.EnablePointerLockInProperty.Value;
        config.EnablePointerLockInProperty.Value = false;
        try
        {
            using var editor = new BoundedEditor(editorCase);
            editor.Reset(195);
            TextBlock header = editor.Host.Require<TextBlock>("PART_HeaderTextBlock");
            Point start = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2),
                editor.Host.Window)!.Value;

            editor.Host.PressAndDrag(header, 40);
            Assert.That(editor.Values, Is.All.EqualTo(200));
            editor.AssertTextMatchesValues();
            Assert.That(editor.Confirmed, Is.Empty);

            // Moving back from the limit must immediately decrease the values again.
            editor.Host.Window.MouseMove(start + new Vector(30, 0), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Values, Is.All.EqualTo(190));
            editor.AssertTextMatchesValues();
            editor.Host.Window.MouseUp(start + new Vector(30, 0), MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(editor.Values) }));
        }
        finally
        {
            config.EnablePointerLockInProperty.Value = pointerLock;
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void Wheel_notifies_once_even_when_the_result_matches_the_draft(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        var changes = new List<object?>();
        editor.Host.Editor.ValueChanged += (_, e) => changes.Add(e.NewValue);
        foreach ((string input, int delta, float proposed, float bounded) in new[]
                 {
                     ("40", -1, 40f, 50f), ("210", 1, 210f, 200f), ("70", 1, 80f, 80f),
                     ("7", 1, 60f, 60f), ("300", -1, 190f, 190f)
                 })
        {
            for (int index = 0; index < editor.VisibleComponentCount; index++)
            {
                float[] initial = editor.Reset(100);
                TextBox box = editor.TextBox(index);
                editor.Host.TypeInto(box, input);
                Assert.That(box.Text, Is.EqualTo(input));
                float[] beforeWheel = editor.Values;
                changes.Clear();

                editor.Host.WheelOver(box, delta);

                float[] expected = editor.WithEditedValue(initial, index, bounded);
                Assert.That(editor.Values, Is.EqualTo(expected));
                editor.AssertTextMatchesValues();
                Assert.That(changes, Is.EqualTo(new[]
                {
                    editor.Compose(editor.WithEditedValue(beforeWheel, index, proposed))
                }));
                Assert.That(editor.Confirmed, Is.Empty);

                editor.Host.MoveFocusToSink();
                Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(expected) }));
            }
        }
    }

    [AvaloniaTest]
    [TestCase("Vector2")]
    [TestCase("Vector3")]
    public void Component_header_drag_changes_only_its_component(string kind)
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool pointerLock = config.EnablePointerLockInProperty.Value;
        config.EnablePointerLockInProperty.Value = false;
        try
        {
            using var editor = new BoundedEditor(new EditorCase(kind));
            for (int index = 0; index < editor.VisibleComponentCount; index++)
            {
                float[] initial = editor.Reset(195);
                TextBlock header = editor.Host.Require<TextBlock>($"PART_Header{BoundedEditor.Components[index]}TextBlock");
                // Component labels overlap the text box's padding; use the exposed left edge.
                Point start = header.TranslatePoint(new Point(1, header.Bounds.Height / 2), editor.Host.Window)!.Value;
                editor.Host.Window.MouseMove(start);
                editor.Host.Window.MouseDown(start, MouseButton.Left);
                editor.Host.Window.MouseMove(start + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
                editor.Host.Window.MouseUp(start + new Vector(40, 0), MouseButton.Left);
                HeadlessTestHelpers.Settle();
                float[] expected = editor.WithEditedValue(initial, index, 200);
                Assert.That(editor.Values, Is.EqualTo(expected));
                editor.AssertTextMatchesValues();
                Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(expected) }));
            }
        }
        finally
        {
            config.EnablePointerLockInProperty.Value = pointerLock;
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_editors))]
    public void External_values_still_update_an_active_component(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        editor.Reset(100);
        TextBox box = editor.TextBox(0);
        editor.Host.TypeInto(box, "7");

        float[] values = editor.Values;
        values[0] = 120;
        editor.Values = values;
        HeadlessTestHelpers.Settle();

        Assert.That(box.Text, Is.EqualTo("120"));
        Assert.That(editor.Values[0], Is.EqualTo(120));
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(SeparateComponents))]
    public void Coercion_can_update_other_components_without_replacing_the_active_text(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase);
        editor.Reset(100);
        editor.Host.Editor.ValueChanged += (_, _) =>
        {
            float[] values = editor.Values;
            values[1] = 120;
            editor.Values = values;
        };

        editor.Host.TypeInto(editor.TextBox(0), "7");

        Assert.That(editor.TextBox(0).Text, Is.EqualTo("7"));
        Assert.That(editor.TextBox(1).Text, Is.EqualTo("120"));
        Assert.That(editor.Values[0], Is.EqualTo(50));
        Assert.That(editor.Values[1], Is.EqualTo(120));
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(NumberAndVectorEditors))]
    public void Decimal_input_is_formatted_only_after_focus_loss(EditorCase editorCase)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            using var editor = new BoundedEditor(editorCase);
            editor.Host.Editor.GetType().GetProperty("NumberFormat")!.SetValue(editor.Host.Editor, "F2");
            float[] initial = editor.Reset(100);
            TextBox box = editor.TextBox(0);
            editor.Host.TypeInto(box, "7");
            editor.Host.Window.KeyTextInput("0");
            editor.Host.Window.KeyTextInput(",");
            Assert.That(box.Text, Is.EqualTo("70,"));
            editor.Host.Window.KeyTextInput("5");
            Assert.That(box.Text, Is.EqualTo("70,5"));
            Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, 0, 70.5f)));

            editor.Host.MoveFocusToSink();
            Assert.That(box.Text, Is.EqualTo("70,50"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(RelativeEditors))]
    public void Unit_only_edits_update_the_unit_before_scrolling(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase, minimum: 0, maximum: 20);
        var changes = new List<object?>();
        editor.Host.Editor.ValueChanged += (_, e) => changes.Add(e.NewValue);
        float[] initial = editor.Reset(0);
        int index = editor.VisibleComponentCount - 1;
        TextBox box = editor.TextBox(index);

        editor.Host.TypeInto(box, "0");
        changes.Clear();
        editor.Host.Window.KeyTextInput("%");
        HeadlessTestHelpers.Settle();
        Assert.That(box.Text, Is.EqualTo("0%"));
        Assert.That(changes, Is.EqualTo(new[] { editor.Compose(initial, RelativeUnit.Relative) }));

        editor.Host.WheelOver(box, 1);
        Assert.That(box.Text, Is.EqualTo($"{10f:f}%"));
        Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 0.1f)).Within(0.00001f));
        editor.Host.MoveFocusToSink();
        Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(editor.Values, RelativeUnit.Relative) }));

        initial = editor.Reset(0);
        changes.Clear();
        editor.Host.TypeInto(box, "0");
        Assert.That(changes, Is.EqualTo(new[] { editor.Compose(initial) }));

        editor.Host.WheelOver(box, 1);
        Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 10)));
        editor.AssertTextMatchesValues();
        editor.Host.MoveFocusToSink();
        Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(editor.Values) }));
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(RelativeEditors))]
    public void Percent_input_and_wheel_keep_relative_units(EditorCase editorCase)
    {
        using var editor = new BoundedEditor(editorCase, minimum: 0.5f, maximum: 2);
        float[] initial = editor.Reset(1);
        int index = editor.VisibleComponentCount - 1;
        TextBox box = editor.TextBox(index);
        editor.Host.TypeInto(box, "7");
        editor.Host.Window.KeyTextInput("0");
        editor.Host.Window.KeyTextInput("%");
        Assert.That(box.Text, Is.EqualTo("70%"));
        float[] expected = editor.WithEditedValue(initial, index, 0.7f);
        Assert.That(editor.Values, Is.EqualTo(expected).Within(0.00001f));

        editor.Host.MoveFocusToSink();
        Assert.That(box.Text, Is.EqualTo($"{70f:f}%"));
        Assert.That(editor.Confirmed, Is.EqualTo(new[] { editor.Compose(editor.Values, RelativeUnit.Relative) }));

        editor.Host.WheelOver(box, 1);
        Assert.That(box.Text, Is.EqualTo($"{80f:f}%"));
        Assert.That(editor.Values, Is.EqualTo(editor.WithEditedValue(initial, index, 0.8f)).Within(0.00001f));
    }

    private sealed class BoundedEditor : IDisposable
    {
        internal static readonly string[] Components = ["First", "Second", "Third", "Fourth"];
        private readonly EditorCase _case;
        private readonly PropertyInfo[] _valueProperties;

        public BoundedEditor(EditorCase editorCase, float minimum = 50, float maximum = 200)
        {
            _case = editorCase;
            PropertyEditor control = editorCase.Kind switch
            {
                "Number" => new NumberEditor<float>(),
                "Vector2" => new Vector2Editor<float> { IsUniform = editorCase.Uniform },
                "Vector3" => new Vector3Editor<float> { IsUniform = editorCase.Uniform },
                "Vector4" => new Vector4Editor<float> { IsUniform = editorCase.Uniform },
                "RelativePoint" => new RelativePointEditor { IsUniform = editorCase.Uniform, Unit = RelativeUnit.Absolute },
                "RelativeRect" => new RelativeRectEditor { IsUniform = editorCase.Uniform, Unit = RelativeUnit.Absolute },
                _ => throw new ArgumentOutOfRangeException(nameof(editorCase)),
            };
            control.Header = $"{editorCase}: 50–200";
            string[] names = editorCase.Kind == "Number" ? ["Value"] : Components.Select(x => x + "Value").ToArray();
            _valueProperties = names.Select(x => control.GetType().GetProperty(x)).OfType<PropertyInfo>().ToArray();
            Assert.That(_valueProperties, Is.Not.Empty);
            Host = new EditorTestHost<PropertyEditor>(control, width: 640);
            // Match the view models: write each property-coerced component back during ValueChanged.
            control.ValueChanged += (_, _) => Values = Values.Select(x => Math.Clamp(x, minimum, maximum)).ToArray();
            control.ValueConfirmed += (_, e) => Confirmed.Add(e.NewValue!);
        }

        public EditorTestHost<PropertyEditor> Host { get; }
        public List<object> Confirmed { get; } = [];
        public int VisibleComponentCount => _case.Uniform ? 1 : _valueProperties.Length;

        public float[] Values
        {
            get => _valueProperties.Select(p => (float)p.GetValue(Host.Editor)!).ToArray();
            set
            {
                for (int i = 0; i < _valueProperties.Length; i++)
                    _valueProperties[i].SetValue(Host.Editor, value[i]);
            }
        }

        public float[] Reset(float value)
        {
            Host.MoveFocusToSink();
            Values = Enumerable.Repeat(value, _valueProperties.Length).ToArray();
            HeadlessTestHelpers.Settle();
            Confirmed.Clear();
            return Values;
        }

        public TextBox TextBox(int index) => Host.Require<TextBox>(_case.Kind == "Number"
            ? "PART_InnerTextBox" : $"PART_Inner{Components[index]}TextBox");

        public float[] WithEditedValue(float[] initial, int index, float value)
            => initial.Select((x, i) => _case.Uniform || i == index ? value : x).ToArray();

        public object Compose(float[] values, RelativeUnit unit = RelativeUnit.Absolute) => _case.Kind switch
        {
            "Number" => values[0],
            "Vector2" => (values[0], values[1]),
            "Vector3" => (values[0], values[1], values[2]),
            "Vector4" => (values[0], values[1], values[2], values[3]),
            "RelativePoint" => new RelativePoint(values[0], values[1], unit),
            "RelativeRect" => new RelativeRect(values[0], values[1], values[2], values[3], unit),
            _ => throw new InvalidOperationException(),
        };

        public void AssertTextMatchesValues()
        {
            float[] values = Values;
            for (int i = 0; i < VisibleComponentCount; i++)
                Assert.That(TextBox(i).Text, Is.EqualTo(values[i].ToString(CultureInfo.CurrentCulture)));
        }

        public void Dispose() => Host.Dispose();
    }
}
