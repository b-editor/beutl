using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.E2ETests.Controls;

public class AlignmentEditorTests
{
    [AvaloniaTest]
    public void AlignmentX_clicking_a_radio_button_sets_value_and_raises_events()
    {
        var editor = new AlignmentXEditor { Header = "Align" };
        var host = new EditorTestHost<AlignmentXEditor>(editor);

        var changed = new List<AlignmentX>();
        var confirmed = new List<AlignmentX>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<AlignmentX>)e).NewValue);
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<AlignmentX>)e).NewValue);

        Assert.That(editor.Value, Is.EqualTo(AlignmentX.Left));

        host.Click(host.Require<RadioButton>("PART_CenterRadioButton"));
        Assert.That(editor.Value, Is.EqualTo(AlignmentX.Center));

        host.Click(host.Require<RadioButton>("PART_RightRadioButton"));
        Assert.That(editor.Value, Is.EqualTo(AlignmentX.Right));

        Assert.That(changed, Is.EqualTo(new[] { AlignmentX.Center, AlignmentX.Right }));
        Assert.That(confirmed, Is.EqualTo(new[] { AlignmentX.Center, AlignmentX.Right }));
    }

    [AvaloniaTest]
    public void AlignmentY_clicking_a_radio_button_sets_value()
    {
        var editor = new AlignmentYEditor { Header = "Align" };
        var host = new EditorTestHost<AlignmentYEditor>(editor);

        Assert.That(editor.Value, Is.EqualTo(AlignmentY.Top));

        host.Click(host.Require<RadioButton>("PART_BottomRadioButton"));
        Assert.That(editor.Value, Is.EqualTo(AlignmentY.Bottom));

        host.Click(host.Require<RadioButton>("PART_CenterRadioButton"));
        Assert.That(editor.Value, Is.EqualTo(AlignmentY.Center));
    }

    [AvaloniaTest]
    public void AlignmentX_setting_value_checks_the_matching_radio_button()
    {
        var editor = new AlignmentXEditor { Header = "Align" };
        var host = new EditorTestHost<AlignmentXEditor>(editor);

        editor.Value = AlignmentX.Right;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.That(host.Require<RadioButton>("PART_RightRadioButton").IsChecked, Is.True);
        Assert.That(host.Require<RadioButton>("PART_LeftRadioButton").IsChecked, Is.False);
    }
}
