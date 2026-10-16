using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.E2ETests.Controls;

// The ColorEditor's only input gesture opens a SimpleColorPickerFlyout, which does not open
// deterministically headless, so the value-changed/confirmed event paths are not reachable here;
// this test only covers that the template applies.
[TestFixture]
public class ColorEditorTests
{
    [AvaloniaTest]
    public void Template_applies_and_exposes_the_picker_button()
    {
        var editor = new ColorEditor { Header = "Color" };
        using var host = new EditorTestHost<ColorEditor>(editor);

        host.Require<Button>("PART_ColorPickerButton");
    }

    // Without live preview, the confirmation reports the value from when the picker opened as the old value.
    // The flyout is confirmed directly, since it does not open deterministically headless.
    [AvaloniaTest]
    public void Grading_confirmation_without_live_preview_reports_the_value_at_open()
    {
        var start = new GradingColor(0.1f, 0.2f, 0.3f);
        var end = new GradingColor(0.4f, 0.5f, 0.6f);
        var editor = new GradingColorEditor { Header = "Lift", Value = start, IsLivePreviewEnabled = false };
        using var host = new EditorTestHost<GradingColorEditor>(editor);
        var confirmed = new List<object>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(e);

        host.Require<Button>("PART_ColorPickerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var flyout = FindFlyout<GradingColorPickerFlyout>(editor);
        flyout.Color = end;
        typeof(FAPickerFlyoutBase).GetMethod("OnConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(flyout, null);

        var args = confirmed.Single() as PropertyEditorValueChangedEventArgs<GradingColor>;
        Assert.That(args, Is.Not.Null);
        Assert.That((args!.OldValue, args.NewValue), Is.EqualTo((start, end)));
    }

    private static TFlyout FindFlyout<TFlyout>(PropertyEditor editor)
    {
        for (Type? type = editor.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField("_flyout", BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                return (TFlyout)field.GetValue(editor)!;
        }

        throw new InvalidOperationException("The editor has no _flyout field.");
    }
}
